open System
open Argu

type CLIArguments =
    | [<MainCommand; Unique>] Project of string
    | Fsc_Args
    | Project_Refs
    | [<AltCommandLine("-gp")>] Get_Property of string list
    | NET_FW_References_Path of string list
    | Installed_NET_Frameworks
    | [<AltCommandLine("-f")>] Framework of string
    | [<AltCommandLine("-r")>] Runtime of string
    | [<AltCommandLine("-c")>] Configuration of string
    | [<AltCommandLine("-v")>] Verbose
    | MSBuild of string
    | DotnetCli of string
    | MSBuild_Host of MSBuildHostPicker
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Fsc_Args -> "get fsc arguments"
            | Project_Refs -> "get project references"
            | NET_FW_References_Path _ -> "list the .NET Framework references"
            | Installed_NET_Frameworks -> "list of the installed .NET Frameworks"
            | Verbose -> "verbose log"
            | Framework _ -> "target framework, the TargetFramework msbuild property"
            | Runtime _ -> "target runtime, the RuntimeIdentifier msbuild property"
            | Configuration _ -> "configuration to use (like Debug), the Configuration msbuild property"
            | Get_Property _ -> "msbuild property to get (allow multiple)"
            | MSBuild _ -> "MSBuild path (default \"msbuild\")"
            | DotnetCli _ -> "Dotnet CLI path (default \"dotnet\")"
            | MSBuild_Host _ -> "the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI"
and MSBuildHostPicker =
    | Auto = 1
    | MSBuild  = 2
    | DotnetMSBuild = 3

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

        //Some env var like `MSBUILD_EXE_PATH` override the msbuild used.
        //The dotnet cli (`dotnet`) set these when calling child processes, and
        //is wrong because these override some properties of the called msbuild
        let msbuildEnvVars =
            psi.Environment.Keys
            |> Seq.filter (fun s -> s.StartsWith("msbuild", StringComparison.OrdinalIgnoreCase))
            |> Seq.toList
        for msbuildEnvVar in msbuildEnvVars do
            psi.Environment.Remove(msbuildEnvVar) |> ignore


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

    let projArgRequired =
        match (results.TryGetResult <@ NET_FW_References_Path @>), (results.TryGetResult<@ Installed_NET_Frameworks @>) with
        | None, None -> true
        | _ -> false

    let! proj =
        match results.TryGetResult <@ Project @>, projArgRequired with
        | Some p, true -> Ok p
        | Some _, false -> Error (InvalidArgsState "project argument not expected")
        | None, false ->
            //create the proj file
            Ok (Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.createEnvInfoProj ())
        | None, true ->
            //scan current directory
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

    let! (isDotnetSdk, getProjectInfoBySdk, getFscArgsBySdk) =
        match projPath with
        | ProjectRecognizer.DotnetSdk ->
            Ok (true, getProjectInfo, getFscArgs)
        | ProjectRecognizer.OldSdk ->
#if NETCOREAPP1_0
            Errors.GenericError "unsupported project format on .net core 1.0, use at least .net core 2.0"
            |> Result.Error
#else
            let asFscArgs props =
                let fsc = Microsoft.FSharp.Build.Fsc()
                Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
            Ok (false, getProjectInfoOldSdk, getFscArgsOldSdk (asFscArgs >> Ok))
#endif
        | ProjectRecognizer.Unsupported ->
            Errors.GenericError "unsupported project format"
            |> Result.Error

    let globalArgs =
        [ results.TryGetResult <@ Framework @>, if isDotnetSdk then "TargetFramework" else "TargetFrameworkVersion"
          results.TryGetResult <@ Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let allCmds =
        [ results.TryGetResult <@ Fsc_Args @> |> Option.map (fun _ -> getFscArgsBySdk)
          results.TryGetResult <@ Project_Refs @> |> Option.map (fun _ -> getP2PRefs)
          results.TryGetResult <@ Get_Property @> |> Option.map (fun p -> (fun () -> getProperties p))
          results.TryGetResult <@ NET_FW_References_Path @> |> Option.map (fun props -> (fun () -> Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.getReferencePaths props))
          results.TryGetResult <@ Installed_NET_Frameworks @> |> Option.map (fun _ -> Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild.installedNETFrameworks) ]

    let msbuildPath = results.GetResult(<@ MSBuild @>, defaultValue = "msbuild")
    let dotnetPath = results.GetResult(<@ DotnetCli @>, defaultValue = "dotnet")
    let dotnetHostPicker = results.GetResult(<@ MSBuild_Host @>, defaultValue = MSBuildHostPicker.Auto)

    let cmds = allCmds |> List.choose id

    let! cmd =
        match cmds with
        | [] -> Error (InvalidArgsState "specify one get argument")
        | [x] -> Ok x
        | _ -> Error (InvalidArgsState "specify only one get argument")

    let exec getArgs additionalArgs = attempt {
        let msbuildExec =
            let projDir = Path.GetDirectoryName(projPath)
            let rec msbuildHost host =
                match host with
                | MSBuildHostPicker.MSBuild ->
                    MSBuildExePath.Path msbuildPath
                | MSBuildHostPicker.DotnetMSBuild ->
                    MSBuildExePath.DotnetMsbuild dotnetPath
                | MSBuildHostPicker.Auto ->
                    if isDotnetSdk then
                        msbuildHost MSBuildHostPicker.DotnetMSBuild
                    else
                        msbuildHost MSBuildHostPicker.MSBuild
                | x ->
                    failwithf "Unexpected msbuild host '%A'" x
            msbuild (msbuildHost dotnetHostPicker) (runCmd log projDir)

        let! r =
            projPath
            |> getProjectInfoBySdk log msbuildExec getArgs additionalArgs
            |> Result.mapError ExecutionError

        return r
        }

    let! r = exec cmd globalArgs

    let out =
        match r with
        | FscArgs args -> args
        | P2PRefs args -> args
        | Properties args -> args |> List.map (fun (x,y) -> sprintf "%s=%s" x y)
        | ResolvedP2PRefs args ->
            let optionalTfm t =
                t |> Option.map (sprintf " (%s)") |> Option.defaultValue ""
            args |> List.map (fun r -> sprintf "%s%s" r.ProjectReferenceFullPath (optionalTfm r.TargetFramework))
        | ResolvedNETRefs args -> args
        | InstalledNETFw args -> args

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
