open System
open Argu

type CLIArguments =
    | [<MainCommand; Unique>] Project of string
    | Fsc_Args
    | Project_Refs
    | [<AltCommandLine("-gp")>] Get_Property of string list
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | [<AltCommandLine("-v")>] Verbose
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Fsc_Args -> "get fsc arguments"
            | Project_Refs -> "get project references"
            | Verbose -> "verbose log"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | Get_Property _ -> "msbuild property to get (allow multiple)"

open Dotnet.ProjInfo.Inspect

type Errors =
    | InvalidArgs of Argu.ArguParseException
    | InvalidArgsState of string
    | ProjectFileNotFound of string
    | GenericError of string
    | RaisedException of System.Exception * string
    | ExecutionError of GetProjectInfoErrors<ShellCommandResult>
and ShellCommandResult = ShellCommandResult of workingDir: string * exePath: string * args: string

let parseArgsCommandLine argv =
    try
        let parser = ArgumentParser.Create<CLIArguments>(programName = "proj-info")
        let results = parser.Parse argv
        Ok results
    with
        | :? ArguParseException as ex ->
            Error (InvalidArgs ex)
        | _ ->
            reraise ()

open Railway
open System.IO

let runCmd log workingDir exePath args =
    log (sprintf "running '%s %s'" exePath (args |> String.concat " "))

    let logOut = System.Collections.Concurrent.ConcurrentQueue<string>()
    let logErr = System.Collections.Concurrent.ConcurrentQueue<string>()

    let runProcess (workingDir: string) (exePath: string) (args: string) =
        let psi = System.Diagnostics.ProcessStartInfo()
        psi.FileName <- exePath
        psi.WorkingDirectory <- workingDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.Arguments <- args
        psi.CreateNoWindow <- true
        psi.UseShellExecute <- false

        use p = new System.Diagnostics.Process()
        p.StartInfo <- psi

        p.OutputDataReceived.Add(fun ea -> logOut.Enqueue (ea.Data))

        p.ErrorDataReceived.Add(fun ea -> logErr.Enqueue (ea.Data))

        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()

        let exitCode = p.ExitCode

        exitCode, (workingDir, exePath, args)

    let exitCode, result = runProcess workingDir exePath (args |> String.concat " ")

    log "output:"
    logOut.ToArray()
    |> Array.iter log

    log "error:"
    logErr.ToArray()
    |> Array.iter log

    exitCode, (ShellCommandResult result)

let realMain argv = attempt {

    let! results = parseArgsCommandLine argv

    let log =
        match results.TryGetResult <@ Verbose @> with
        | Some _ -> printfn "%s"
        | None -> ignore

    let! proj =
        match results.TryGetResult <@ Project @> with
        | Some p -> Ok p
        | None ->
            let workDir = Directory.GetCurrentDirectory()
            match Directory.GetFiles(workDir, "*.*proj") |> List.ofArray with
            | [] ->
                Error (InvalidArgsState "no .*proj project found in current directory, use --project argument to specify path")
            | [x] -> Ok x
            | xs ->
                Error (InvalidArgsState "multiple .*proj found in current directory, use --project argument to specify path")

    let projPath = Path.GetFullPath(proj)

    log (sprintf "resolved project path '%s'" projPath)

    do! if not (File.Exists projPath)
        then Error (ProjectFileNotFound projPath)
        else Ok ()

    let! (isDotnetSdk, getFscArgsBySdk) =
        match projPath with
        | ProjectRecognizer.DotnetSdk ->
            Ok (true, getFscArgs)
        | ProjectRecognizer.OldSdk ->
#if NETCOREAPP1_0
            Errors.GenericError "unsupported project format on .net core 1.0, use at least .net core 2.0"
            |> Result.Error
#else
            let asFscArgs props =
                let fsc = Microsoft.FSharp.Build.Fsc()
                Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
            Ok (false, getFscArgsOldSdk (asFscArgs >> Ok))
#endif
        | ProjectRecognizer.Unsupported ->
            Errors.GenericError "unsupported project format"
            |> Result.Error

    let globalArgs =
        [ results.TryGetResult <@ Framework @>, "TargetFramework"
          results.TryGetResult <@ Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let allCmds =
        [ results.TryGetResult <@ Fsc_Args @> |> Option.map (fun _ -> getFscArgsBySdk)
          results.TryGetResult <@ Project_Refs @> |> Option.map (fun _ -> getP2PRefs)
          results.TryGetResult <@ Get_Property @> |> Option.map (fun p -> (fun () -> getProperties p)) ]

    let cmds = allCmds |> List.choose id

    let! cmd =
        match cmds with
        | [] -> Error (InvalidArgsState "specify one get argument")
        | [x] -> Ok x
        | _ -> Error (InvalidArgsState "specify only one get argument")

    let exec getArgs additionalArgs = attempt {
        let msbuildExec =
            let projDir = Path.GetDirectoryName(projPath)
            let msbuildHost =
                if isDotnetSdk then
                    MSBuildExePath.DotnetMsbuild "dotnet"
                else
                    MSBuildExePath.Path "msbuild"
            msbuild msbuildHost (runCmd log projDir)

        let! r =
            if isDotnetSdk then
                projPath
                |> getProjectInfo log msbuildExec getArgs additionalArgs
                |> Result.mapError ExecutionError
            else
                projPath
                |> getProjectInfoOldSdk log msbuildExec getArgs additionalArgs
                |> Result.mapError ExecutionError

        return r
        }

    let! r = exec cmd globalArgs

    let out =
        match r with
        | FscArgs args -> args
        | P2PRefs args -> args
        | Properties args -> args |> List.map (fun (x,y) -> sprintf "%s=%s" x y)
        | ResolvedP2PRefs _ -> []

    out |> List.iter (printfn "%s")

    return r
    }

let wrapEx m f a =
    try
        f a
    with ex ->
        Error (RaisedException (ex, m))

[<EntryPoint>]
let main argv =
    match wrapEx "uncaught exception" (realMain >> runAttempt) argv with
    | Ok _ -> 0
    | Error err ->
        match err with
        | InvalidArgs ex ->
            printfn "%s" (ex.Message)
            1
        | InvalidArgsState message ->
            printfn "%s" message
            printfn "see --help for more info"
            2
        | ProjectFileNotFound projPath ->
            printfn "project file '%s' not found" projPath
            3
        | GenericError message ->
            printfn "%s" message
            4
        | RaisedException (ex, message) ->
            printfn "%s:" message
            printfn "%A" ex
            6
        | ExecutionError (MSBuildFailed (i, r)) ->
            printfn "%i %A" i r
            7
        | ExecutionError (UnexpectedMSBuildResult r) ->
            printfn "%A" r
            8
        | ExecutionError (MSBuildSkippedTarget) ->
            printfn "internal error, target was skipped"
            9
