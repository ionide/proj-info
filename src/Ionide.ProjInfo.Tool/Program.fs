// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Argu
open Ionide.ProjInfo
open Ionide.ProjInfo.Types

type Args =
    | Version
    | Project of path: string
    | Solution of path: string
    | Graph
    | Fcs
    | Serialize
    interface IArgParserTemplate with
        member x.Usage =
            match x with
            | Version -> "Display the version of the application"
            | Project (path) -> "Analyze a single project at {path}"
            | Solution (path) -> "Analyze a solution of projects at {path}"
            | Graph -> "Use the graph loader"
            | Fcs -> "Map project to FSharpProjectOptions"
            | Serialize -> "Serialize the project to JSON"

let parser =
    Argu.ArgumentParser.Create("proj", "analyze msbuild projects", errorHandler = ProcessExiter(), checkStructure = true)

type LoaderFunc = ToolsPath * list<string * string> -> IWorkspaceLoader

let (|Rooted|) (s: string) =
    System.IO.Path.Combine(System.Environment.CurrentDirectory, s)

let parseProject (loaderFunc: LoaderFunc) (Rooted path) =
    let cwd = System.IO.Path.GetDirectoryName path |> System.IO.DirectoryInfo
    let toolsPath = Ionide.ProjInfo.Init.init cwd None
    let loader = loaderFunc (toolsPath, [])
    loader.LoadProjects([ path ], [], BinaryLogGeneration.Within cwd)

let parseSolution (loaderFunc: LoaderFunc) (Rooted path) =
    let cwd = System.IO.Path.GetDirectoryName path |> System.IO.DirectoryInfo
    let toolsPath = Ionide.ProjInfo.Init.init cwd None
    let loader = loaderFunc (toolsPath, [])
    loader.LoadSln(path, [], BinaryLogGeneration.Within cwd)

[<EntryPoint>]
let main argv =
    let args = parser.ParseCommandLine(argv, raiseOnUsage = false)

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

        let loaderFunc: LoaderFunc =
            match args.TryGetResult Graph with
            | Some _ -> fun (p, opts) -> WorkspaceLoaderViaProjectGraph.Create(p, opts)
            | None -> fun (p, opts) -> WorkspaceLoader.Create(p, opts)

        let projects =
            match args.TryGetResult Project with
            | Some path -> parseProject loaderFunc path
            | None ->
                match args.TryGetResult Solution with
                | Some path -> parseSolution loaderFunc path
                | None -> Seq.empty

        let projects = projects |> List.ofSeq
        let shouldSerialize = args.Contains Serialize

        match projects with
        | [] ->
            failwith "Couldn't parse any projects"
            exit 1
        | projects ->
            if args.Contains Fcs then
                let projects = projects |> List.map (fun p -> Ionide.ProjInfo.FCS.mapToFSharpProjectOptions p projects)

                if shouldSerialize then
                    projects |> Newtonsoft.Json.JsonConvert.SerializeObject |> printfn "%s"
                else
                    printfn "%A" projects

            else if shouldSerialize then
                projects |> Newtonsoft.Json.JsonConvert.SerializeObject |> printfn "%s"
            else
                printfn "%A" projects


            exit 0
