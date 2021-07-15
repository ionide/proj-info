// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Argu

type Args =
    | Version
    | Project of path: string
    | Solution of path: string
    | Graph
    interface IArgParserTemplate with
        member x.Usage =
            match x with
            | Version -> "Display the version of the application"
            | Project (path) -> "Analyze a single project at {path}"
            | Solution (path) -> "Analyze a solution of projects at {path}"
            | Graph -> "Use the graph loader"

let parser = Argu.ArgumentParser.Create("proj", "analyze msbuild projects", errorHandler = ProcessExiter())

let mutable loaderFunc = Ionide.ProjInfo.WorkspaceLoader.Create

let parseProject (path: string) =
    let cwd = System.IO.Path.GetDirectoryName path
    let toolsPath = Ionide.ProjInfo.Init.init cwd
    let loader = loaderFunc toolsPath
    loader.LoadProjects([ path ], [], true)

let parseSolution (path: string) =
    let cwd = System.IO.Path.GetDirectoryName path
    let toolsPath = Ionide.ProjInfo.Init.init cwd
    let loader = loaderFunc toolsPath
    loader.LoadSln path

[<EntryPoint>]
let main argv =
    let args = parser.ParseCommandLine(argv, false, true, false)

    if args.TryGetResult Version <> None then
        printfn
            $"Ionide.ProjInfo.Tool, v%A{System
                                            .Reflection
                                            .Assembly
                                            .GetExecutingAssembly()
                                            .GetName()
                                            .Version}"

        0
    else
        args.TryGetResult Graph |> Option.iter (fun _ -> loaderFunc <- Ionide.ProjInfo.WorkspaceLoaderViaProjectGraph.Create)

        let projects =
            match args.TryGetResult Project with
            | Some path -> parseProject path
            | None ->
                match args.TryGetResult Solution with
                | Some path -> parseSolution path
                | None -> Seq.empty

        let projects = projects |> List.ofSeq

        match projects with
        | [] ->
            failwith "Couldn't parse any projects"
            exit 1
        | projects ->
            printfn "%A" projects
            exit 0
