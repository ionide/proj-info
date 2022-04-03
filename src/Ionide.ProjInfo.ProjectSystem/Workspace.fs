module Ionide.ProjInfo.ProjectSystem.Workspace

open Ionide.ProjInfo.Types
open Ionide.ProjInfo
open System.IO

type internal GetProjectOptionsErrors = Types.GetProjectOptionsErrors

[<RequireQualifiedAccess>]
type internal ProjectSystemState =
    | Loading of string
    | Loaded of FSharp.Compiler.CodeAnalysis.FSharpProjectOptions * Types.ProjectOptions * ProjectViewerItem list * fromDpiCache: bool
    | LoadedOther of Types.ProjectOptions * ProjectViewerItem list * fromDpiCache: bool
    | Failed of string * GetProjectOptionsErrors


let private getItems isFromCache fcsPo po =
    let view = ProjectViewer.render po

    let items =
        if obj.ReferenceEquals(view.Items, null) then
            []
        else
            view.Items

    items

let private getProjectOptions (loader: IWorkspaceLoader) (onEvent: ProjectSystemState -> unit) binaryLogs (projectFileNames: string list) =
    let existing, notExisting = projectFileNames |> List.partition (File.Exists)

    for e in notExisting do
        let error = GenericError(e, sprintf "File '%s' does not exist" e)
        onEvent (ProjectSystemState.Failed(e, error))

    let handler state =
        match state with
        | WorkspaceProjectState.Failed (projectFileName, error) -> onEvent (ProjectSystemState.Failed(projectFileName, error))
        | WorkspaceProjectState.Loading (p) -> onEvent (ProjectSystemState.Loading p)
        | WorkspaceProjectState.Loaded (po, allProjects, isFromCache) when po.ProjectFileName.EndsWith ".fsproj" ->
            let fpo = FCS.mapToFSharpProjectOptions po allProjects
            let items = getItems isFromCache fpo po
            onEvent (ProjectSystemState.Loaded(fpo, po, items, isFromCache))
        | WorkspaceProjectState.Loaded (po, allProjects, isFromCache) ->
            let view = ProjectViewer.render po
            let items = view.Items
            onEvent (ProjectSystemState.LoadedOther(po, items, isFromCache))

    use notif = loader.Notifications.Subscribe handler
    loader.LoadProjects(existing, [], binaryLogs) |> ignore // TODO: Maybe we should move away from event driven approach???

let internal loadInBackground onLoaded (loader: IWorkspaceLoader) (projects: Project list) binaryLogs =
    let (resProjects, otherProjects) = projects |> List.partition (fun n -> n.Response.IsSome)

    for project in resProjects do
        match project.Response with
        | Some res ->
            // if we have project data already then that means it was cached.
            // fire a loading/loaded event pair so that outside observers get the correct loading experience
            onLoaded (ProjectSystemState.Loading project.FileName )
            onLoaded (ProjectSystemState.Loaded(res.Options, res.ExtraInfo, res.Items, true))
        | None -> () //Shouldn't happen

    otherProjects |> List.map (fun n -> n.FileName) |> getProjectOptions loader onLoaded binaryLogs
