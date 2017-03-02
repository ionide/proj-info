open System
open Argu

type CLIArguments =
    | [<Mandatory; Unique; AltCommandLine("-p")>] Project of string
    | Fsc_Args
    | Project_Refs
    | [<AltCommandLine("-v")>] Verbose
with
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Project _ -> "the MSBuild project file"
            | Fsc_Args -> "get fsc arguments"
            | Project_Refs -> "get project references"
            | Verbose -> "verbose log"

open Railway

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

open Inspect

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

    let! _ = install_target_file log projPath

    let msbuildExec args =
        let cmd = dotnetMsbuild (runCmd log) (projPath :: args)
        log "output:"
        cmd.StandardOutput.ReadToEnd()
        |> log
        log "error:"
        cmd.StandardError.ReadToEnd()
        |> log

        cmd.Result

    let exec getArgs () =
        let tmp = System.IO.Path.GetTempFileName()
        let args, parse = getArgs tmp
        let result = msbuildExec args
        if result.Success then
            parse ()
        else
            Error result

    let cmds =
        [ <@ Fsc_Args @>, (exec getFscArgs)
          <@ Project_Refs @>, (exec getP2PRefsArgs) ]

    let! cmd =
        match cmds |> List.filter (fst >> results.TryGetResult >> Option.isSome) with
        | [] -> Error (InvalidArgsState "specify one get argument")
        | [x] -> Ok x
        | _ -> Error (InvalidArgsState "specify only one get argument")

    let c, f = cmd

    let r = f ()

    let out =
        match r with
        | Ok (FscArgs args) -> args
        | Ok (P2PRefs args) -> args
        | Error _ -> []

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
