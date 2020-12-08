namespace Dotnet.ProjInfo.ProjectSystem

open System
open System.IO
open System.Collections.Concurrent
open FSharp.Compiler.SourceCodeServices
open Dotnet.ProjInfo.Types
open Dotnet.ProjInfo
open Workspace

type ProjectResult =
    { ProjectFileName: string
      ProjectFiles: List<string>
      OutFileOpt: string option
      References: string list
      Extra: ProjectOptions
      ProjectItems: ProjectViewerItem list
      Additionals: Map<string, string> }

[<RequireQualifiedAccess>]
type ProjectResponse =
    | Project of project: ProjectResult * isFromCache: bool
    | ProjectChanged of projectFileName: string
    | ProjectError of projectFileName: string * errorDetails: GetProjectOptionsErrors
    | ProjectLoading of projectFileName: string
    | WorkspaceLoad of finished: bool
    member x.DebugPrint =
        match x with
        | Project (po, _) -> "Loaded: " + po.ProjectFileName
        | ProjectChanged path -> "Changed: " + path
        | ProjectError (path, _) -> "Failed: " + path
        | ProjectLoading path -> "Loading: " + path
        | WorkspaceLoad status -> sprintf "Workspace Status: %b" status

/// Public API for any operations related to workspace and projects.
/// Internally keeps all the information related to project files in current workspace.
/// It's responsible for refreshing and caching - should be used only as source of information and public API
type ProjectController(checker: FSharpChecker, toolsPath: ToolsPath) =
    let fileCheckOptions = ConcurrentDictionary<string, FSharpProjectOptions>()
    let projects = ConcurrentDictionary<string, Project>()
    let mutable isWorkspaceReady = false
    let workspaceReady = Event<unit>()
    let notify = Event<ProjectResponse>()

    let updateState (response: ProjectCrackerCache) =
        let normalizeOptions (opts: FSharpProjectOptions) =
            { opts with
                  SourceFiles = opts.SourceFiles |> Array.filter (FscArguments.isCompileFile) |> Array.map (Path.GetFullPath)
                  OtherOptions =
                      opts.OtherOptions
                      |> Array.map
                          (fun n ->
                              if FscArguments.isCompileFile (n)
                              then Path.GetFullPath n
                              else n) }

        for file in response.Items
                    |> List.choose
                        (function
                        | ProjectViewerItem.Compile (p, _) -> Some p) do
            fileCheckOptions.[file] <- normalizeOptions response.Options

    member private x.loadProjects (files: string list) (generateBinlog: bool) =
        async {
            let onChange fn =
                ProjectResponse.ProjectChanged fn |> notify.Trigger
                x.LoadProject(fn, generateBinlog)

            let onLoaded p =
                match p with
                | ProjectSystemState.Loading projectFileName -> ProjectResponse.ProjectLoading projectFileName |> notify.Trigger
                | ProjectSystemState.Failed (projectFileName, error) -> ProjectResponse.ProjectError(projectFileName, error) |> notify.Trigger
                | ProjectSystemState.Loaded (opts, extraInfo, projectFiles, isFromCache) ->
                    let response = ProjectCrackerCache.create (opts, extraInfo, projectFiles)
                    let projectFileName = response.ProjectFileName

                    let project =
                        match projects.TryFind projectFileName with
                        | Some prj -> prj
                        | None ->
                            let proj = new Project(projectFileName, onChange)
                            projects.[projectFileName] <- proj
                            proj

                    project.Response <- Some response

                    updateState response

                    let responseFiles =
                        response.Items
                        |> List.choose
                            (function
                            | ProjectViewerItem.Compile (p, _) -> Some p)

                    let projInfo: ProjectResult =
                        { ProjectFileName = projectFileName
                          ProjectFiles = responseFiles
                          OutFileOpt = response.OutFile
                          References = response.References
                          Extra = response.ExtraInfo
                          ProjectItems = projectFiles
                          Additionals = Map.empty }

                    ProjectResponse.Project(projInfo, isFromCache) |> notify.Trigger
                | ProjectSystemState.LoadedOther (extraInfo, projectFiles, fromDpiCache) ->
                    let responseFiles =
                        projectFiles
                        |> List.choose
                            (function
                            | ProjectViewerItem.Compile (p, _) -> Some p)

                    let projInfo: ProjectResult =
                        { ProjectFileName = extraInfo.ProjectFileName
                          ProjectFiles = responseFiles
                          OutFileOpt = Some(extraInfo.TargetPath)
                          References = FscArguments.references extraInfo.OtherOptions
                          Extra = extraInfo
                          ProjectItems = projectFiles
                          Additionals = Map.empty }

                    ProjectResponse.Project(projInfo, fromDpiCache) |> notify.Trigger


            //TODO check full path
            let projectFileNames = files |> List.map Path.GetFullPath

            let prjs = projectFileNames |> List.map (fun projectFileName -> projectFileName, new Project(projectFileName, onChange))

            for projectFileName, proj in prjs do
                projects.[projectFileName] <- proj


            ProjectResponse.WorkspaceLoad false |> notify.Trigger

            // this is to delay the project loading notification (of this thread)
            // after the workspaceload started response returned below in outer async
            // Make test output repeteable, and notification in correct order
            match Environment.workspaceLoadDelay () with
            | delay when delay > TimeSpan.Zero -> do! Async.Sleep(Environment.workspaceLoadDelay().TotalMilliseconds |> int)
            | _ -> ()

            let loader = WorkspaceLoader.Create(toolsPath)

            let bindNewOnloaded (n: WorkspaceProjectState): ProjectSystemState option =
                match n with
                | WorkspaceProjectState.Loading (path) -> Some(ProjectSystemState.Loading path)
                | WorkspaceProjectState.Loaded (opts, allKNownProjects, isFromCache) ->
                    let fcsOpts = FCS.mapToFSharpProjectOptions opts allKNownProjects

                    match Workspace.extractOptionsDPW fcsOpts with
                    | Ok optsDPW ->
                        let view = ProjectViewer.render optsDPW
                        Some(ProjectSystemState.Loaded(fcsOpts, optsDPW, view.Items, isFromCache))
                    | Error _ -> None //TODO not ignore the error
                | WorkspaceProjectState.Failed (path, e) ->
                    let error = e
                    Some(ProjectSystemState.Failed(path, error))

            // loader.Notifications.Add(fun arg -> arg |> bindNewOnloaded |> Option.iter onLoaded)

            Workspace.loadInBackground onLoaded loader (prjs |> List.map snd) generateBinlog

            ProjectResponse.WorkspaceLoad true |> notify.Trigger

            isWorkspaceReady <- true
            workspaceReady.Trigger()

            return true
        }

    member private x.LoaderLoop =
        MailboxProcessor.Start
            (fun agent ->
                let rec loop () =
                    async {
                        let! (fn, gb) = agent.Receive()
                        let! _ = x.loadProjects fn gb
                        return ()
                    }

                loop ())

    member __.WorkspaceReady = workspaceReady.Publish

    member __.Notifications = notify.Publish

    member __.IsWorkspaceReady = isWorkspaceReady

    member __.GetProjectOptions(file: string): FSharpProjectOptions option =
        let file = Utils.normalizePath file
        fileCheckOptions.TryFind file

    member __.SetProjectOptions(file: string, opts: FSharpProjectOptions) =
        let file = Utils.normalizePath file
        fileCheckOptions.AddOrUpdate(file, (fun _ -> opts), (fun _ _ -> opts)) |> ignore

    member __.RemoveProjectOptions(file) =
        let file = Utils.normalizePath file
        fileCheckOptions.TryRemove file |> ignore

    member __.ProjectOptions = fileCheckOptions |> Seq.map (|KeyValue|)

    member x.LoadProject(projectFileName: string, generateBinlog: bool) =
        x.LoaderLoop.Post([ projectFileName ], generateBinlog)

    member x.LoadProject(projectFileName: string) = x.LoadProject(projectFileName, false)

    member x.LoadWorkspace(files: string list, generateBinlog: bool) = x.LoaderLoop.Post(files, generateBinlog)

    member x.LoadWorkspace(files: string list) = x.LoadWorkspace(files, false)

    member __.PeekWorkspace(dir: string, deep: int, excludedDirs: string list) = WorkspacePeek.peek dir deep excludedDirs
