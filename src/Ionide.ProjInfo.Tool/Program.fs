// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Argu

type Args =
    | Version
    | Project of path: string
    | Solution of path: string
    interface IArgParserTemplate with
        member x.Usage =
            match x with
            | Version -> "Display the version of the application"
            | Project (path) -> "Analyze a single project at {path}"
            | Solution (path) -> "Analyze a solution of projects at {path}"

let parser = Argu.ArgumentParser.Create("proj", "analyze msbuild projects", errorHandler = ProcessExiter())

let parseProject (path: string) =
    let toolsPath = Ionide.ProjInfo.Init.init ()
    let loader = Ionide.ProjInfo.WorkspaceLoaderViaProjectGraph.Create toolsPath
    let [ project ] = loader.LoadProjects [ path ] |> Seq.toList
    project

let parseSolution (path: string) =
    let toolsPath = Ionide.ProjInfo.Init.init ()
    let loader = Ionide.ProjInfo.WorkspaceLoaderViaProjectGraph.Create toolsPath
    let projects = loader.LoadSln path
    projects

[<EntryPoint>]
let main argv =
    let args = parser.ParseCommandLine(argv, false, true, false)

    if args.TryGetResult Version <> None then
        printfn
            $"Ionide.ProjInfo.Tool, v%A{
                                            System
                                                .Reflection
                                                .Assembly
                                                .GetExecutingAssembly()
                                                .GetName()
                                                .Version
            }"

        0
    else
        match args.TryGetResult Project with
        | Some path ->
            let project = parseProject path
            printfn $"%A{project}"
            0
        | None ->
            match args.TryGetResult Solution with
            | Some path ->
                let projects = parseSolution path
                printfn $"%A{projects}"
                0
            | None -> 2
