open System
open Argu

type CLIArguments =
    | [<MainCommand; Mandatory; Unique>] Project of string
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

open Inspect

type Errors =
    | InvalidArgs of Argu.ArguParseException
    | InvalidArgsState of string
    | ProjectFileNotFound of string
    | GenericError of string
    | RaisedException of System.Exception * string
    | ExecutionError of GetProjectInfoErrors<Medallion.Shell.CommandResult>

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

open Medallion.Shell
open Railway

let runCmd log exePath args =
    log (sprintf "running '%s %s'" exePath (args |> String.concat " "))
    let cmd = Command.Run(exePath, args |> Array.ofList |> Array.map box)

    let result = cmd.Result
    log "output:"
    cmd.StandardOutput.ReadToEnd() |> log

    log "error:"
    cmd.StandardError.ReadToEnd() |> log

    result.ExitCode, result

let realMain argv = attempt {

    let! results = parseArgsCommandLine argv

    let log =
        match results.TryGetResult <@ Verbose @> with
        | Some _ -> printfn "%s"
        | None -> ignore

    let! proj =
        match results.TryGetResult <@ Project @> with
        | Some p -> Ok p
        | None -> Error (InvalidArgsState "--project argument is required")

    let projPath = System.IO.Path.GetFullPath(proj)

    log (sprintf "resolved project path '%s'" projPath)

    do! if not (System.IO.File.Exists projPath)
        then Error (ProjectFileNotFound projPath)
        else Ok ()

    let globalArgs =
        [ results.TryGetResult <@ Framework @>, "TargetFramework"
          results.TryGetResult <@ Runtime @>, "RuntimeIdentifier"
          results.TryGetResult <@ Configuration @>, "Configuration" ]
        |> List.choose (fun (a,p) -> a |> Option.map (fun x -> (p,x)))
        |> List.map (MSBuild.MSbuildCli.Property)

    let allCmds =
        [ results.TryGetResult <@ Fsc_Args @> |> Option.map (fun _ -> getFscArgs)
          results.TryGetResult <@ Project_Refs @> |> Option.map (fun _ -> getP2PRefs)
          results.TryGetResult <@ Get_Property @> |> Option.map (fun p -> (fun () -> getProperties p)) ]

    let cmds = allCmds |> List.choose id

    let! cmd =
        match cmds with
        | [] -> Error (InvalidArgsState "specify one get argument")
        | [x] -> Ok x
        | _ -> Error (InvalidArgsState "specify only one get argument")

    let exec getArgs additionalArgs = attempt {
        let msbuildExec = dotnetMsbuild (runCmd log)

        let! r =
            projPath
            |> getProjectInfo log msbuildExec [getArgs] additionalArgs
            |> Result.mapError ExecutionError

        return r |> List.map (Result.mapError ExecutionError)
        }

    let! results = exec cmd globalArgs

    let! r =
        match results with
        | [x] -> x
        | xs -> Error (GenericError (sprintf "unexpected multiple results from msbuild '%A'" xs))

    let out =
        match r with
        | FscArgs args -> args
        | P2PRefs args -> args
        | Properties args -> args |> List.map (fun (x,y) -> sprintf "%s=%s" x y)
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
