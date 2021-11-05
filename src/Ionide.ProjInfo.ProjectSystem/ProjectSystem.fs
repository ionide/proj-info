namespace Ionide.ProjInfo.ProjectSystem

open System
open System.IO
open System.Collections.Concurrent
open FSharp.Compiler.CodeAnalysis
open Ionide.ProjInfo.Types
open Ionide.ProjInfo
open Workspace
open FSharp.Control.Reactive

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
type ProjectController(toolsPath: ToolsPath, workspaceLoaderFactory: ToolsPath -> IWorkspaceLoader) as x =
    let fileCheckOptions = ConcurrentDictionary<string, FSharpProjectOptions>()
    let projects = ConcurrentDictionary<string, Project>()
    let mutable isWorkspaceReady = false
    let workspaceReady = Event<unit>()
    let notify = Event<ProjectResponse>()


    let deduplicateBy keySelector (obs: IObservable<'a>) =
        obs
        |> Observable.synchronize // deals with concurrency issues
        |> Observable.groupByUntil (keySelector) (Observable.throttle (TimeSpan.FromMilliseconds 1000.)) // Groups and Debounces by keySelector.  We use `groupByUntil` instead of just `groupBy` because it forces streams to close which if left open forever could cause memory issues.
        |> Observable.bind (Observable.takeLast 1) // Takes the last in the debounced grouping and merged all streams back together with `bind`
        |> Observable.bufferSpan (TimeSpan.FromMilliseconds 1000.)
        |> Observable.map (List.ofSeq >> List.distinct) // Buffers the file changes together

    // This gets pushed to every time `onChange` get called in `loadProjects`
    let projectsChanged = new System.Reactive.Subjects.Subject<string * BinaryLogGeneration>()

    let sub =
        let loadProjects (projs: list<string * BinaryLogGeneration>) =
            let projectGroups = projs |> List.groupBy snd |> List.map (fun (k, vs) -> k, vs |> List.map fst)
            projs |> List.iter (fun (fileName, _) -> fileName |> ProjectResponse.ProjectChanged |> notify.Trigger)

            for (key, group) in projectGroups do
                x.LoadWorkspace(group, key)

        projectsChanged |> deduplicateBy fst |> Observable.subscribe loadProjects


    let updateState (response: ProjectCrackerCache) =
        let normalizeOptions (opts: FSharpProjectOptions) =
            { opts with
                  SourceFiles =
                    opts.SourceFiles
                    |> Array.filter (FscArguments.isCompileFile)
                    |> Array.map (Path.GetFullPath)
                    |> Array.map (fun p -> (p.Chars 0).ToString().ToLower() + p.Substring(1))
                  OtherOptions =
                      opts.OtherOptions
                      |> Array.map
                          (fun n ->
                              if FscArguments.isCompileFile (n) then
                                  Path.GetFullPath n
                              else
                                  n) }

        for file in
            response.Items
            |> List.choose
                (function
                | ProjectViewerItem.Compile (p, _) -> Some p) do
            fileCheckOptions.[file] <- normalizeOptions response.Options


    member private x.loadProjects (files: string list) (binaryLogs: BinaryLogGeneration) =
        async {
            let onChange fn = projectsChanged.OnNext(fn, binaryLogs)

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

            let loader = workspaceLoaderFactory toolsPath
            Workspace.loadInBackground onLoaded loader (prjs |> List.map snd) binaryLogs
            ProjectResponse.WorkspaceLoad true |> notify.Trigger

            isWorkspaceReady <- true
            workspaceReady.Trigger()

            return true
        }

    member private x.LoaderLoop =
        MailboxProcessor.Start
            (fun agent -> //If couldn't recive new event in 50 ms then just load previous one
                let rec loop (previousStatus: (string list * BinaryLogGeneration) option) =
                    async {
                        match previousStatus with

                        | Some (fn, gb) ->
                            match! agent.TryReceive(50) with
                            | None -> //If couldn't recive new event in 50 ms then just load previous one
                                let! _ = x.loadProjects fn gb
                                return! loop None
                            | Some (fn2, gb2) when fn2 = fn -> //If recived same load request then wait again (in practice shouldn't happen more than 2 times)
                                return! loop previousStatus
                            | Some (fn2, gb2) -> //If recived some other project load previous one, and then wait with the new one
                                let! _ = x.loadProjects fn gb
                                return! loop (Some(fn2, gb2))
                        | None ->
                            let! (fn, gb) = agent.Receive()
                            return! loop (Some(fn, gb))
                    }

                loop None)

    ///Event notifies that whole workspace has been loaded
    member __.WorkspaceReady = workspaceReady.Publish

    ///Event notifies about any loading events
    member __.Notifications = notify.Publish

    member __.IsWorkspaceReady = isWorkspaceReady

    ///Try to get instance of `FSharpProjectOptions` for given `.fs` file
    member __.GetProjectOptions(file: string) : FSharpProjectOptions option =
        let file = Utils.normalizePath file
        fileCheckOptions.TryFind file

    member __.SetProjectOptions(file: string, opts: FSharpProjectOptions) =
        let file = Utils.normalizePath file
        fileCheckOptions.AddOrUpdate(file, (fun _ -> opts), (fun _ _ -> opts)) |> ignore

    member __.RemoveProjectOptions(file) =
        let file = Utils.normalizePath file
        fileCheckOptions.TryRemove file |> ignore

    ///Try to get instance of `FSharpProjectOptions` for given `.fsproj` file
    member __.GetProjectOptionsForFsproj(fsprojPath: string) : FSharpProjectOptions option =
        fileCheckOptions.Values |> Seq.tryFind (fun n -> n.ProjectFileName = fsprojPath)

    ///Returns a sequance of all known path-to-`.fs` * `FSharpProjectOptions` pairs
    member __.ProjectOptions = fileCheckOptions |> Seq.map (|KeyValue|)

    ///Loads a single project file
    member x.LoadProject(projectFileName: string, binaryLogs: BinaryLogGeneration) =
        x.LoaderLoop.Post([ projectFileName ], binaryLogs)

    ///Loads a single project file
    member x.LoadProject(projectFileName: string) =
        x.LoadProject(projectFileName, BinaryLogGeneration.Off)

    ///Loads a set of project files
    member x.LoadWorkspace(files: string list, binaryLogs: BinaryLogGeneration) = x.LoaderLoop.Post(files, binaryLogs)

    ///Loads a set of project files
    member x.LoadWorkspace(files: string list) =
        x.LoadWorkspace(files, BinaryLogGeneration.Off)

    ///Finds a list of potential workspaces (solution files/lists of projects) in given dir
    member __.PeekWorkspace(dir: string, deep: int, excludedDirs: string list) =
        WorkspacePeek.peek dir deep excludedDirs

    interface IDisposable with
        member _.Dispose() =
            sub.Dispose()
            projectsChanged.Dispose()
