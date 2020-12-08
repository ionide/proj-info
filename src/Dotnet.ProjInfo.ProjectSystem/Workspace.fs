module Dotnet.ProjInfo.ProjectSystem.Workspace

open Dotnet.ProjInfo.Types
open Dotnet.ProjInfo
open System.IO

type internal GetProjectOptionsErrors = Types.GetProjectOptionsErrors

[<RequireQualifiedAccess>]
type internal ProjectSystemState =
    | Loading of string
    | Loaded of FSharp.Compiler.SourceCodeServices.FSharpProjectOptions * Types.ProjectOptions * ProjectViewerItem list * fromDpiCache: bool
    | LoadedOther of Types.ProjectOptions * ProjectViewerItem list * fromDpiCache: bool
    | Failed of string * GetProjectOptionsErrors


let internal extractOptionsDPW (opts: FSharp.Compiler.SourceCodeServices.FSharpProjectOptions) =
    match opts.ExtraProjectInfo with
    | None -> Error(GenericError(opts.ProjectFileName, "expected ExtraProjectInfo after project parsing, was None"))
    | Some x ->
        match x with
        | :? ProjectOptions as poDPW -> Ok poDPW
        | x -> Error(GenericError(opts.ProjectFileName, (sprintf "expected ExtraProjectInfo after project parsing, was %A" x)))

let private bindResults isFromCache res =
    extractOptionsDPW res
    |> Result.bind
        (fun optsDPW ->
            let view = ProjectViewer.render optsDPW

            let items =
                if obj.ReferenceEquals(view.Items, null)
                then []
                else view.Items

            Result.Ok(res, optsDPW, items, isFromCache))

let private getProjectOptions (loader: WorkspaceLoader) (onEvent: ProjectSystemState -> unit) (generateBinlog: bool) (projectFileNames: string list) =
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
            let x = bindResults isFromCache fpo

            match x with
            | Ok (opts, optsDPW, projViewerItems, isFromCache) -> onEvent (ProjectSystemState.Loaded(opts, optsDPW, projViewerItems, isFromCache))
            | Error error -> onEvent (ProjectSystemState.Failed(error.ProjFile, error))
        | WorkspaceProjectState.Loaded (po, allProjects, isFromCache) ->
            let view = ProjectViewer.render po
            let items = view.Items
            onEvent (ProjectSystemState.LoadedOther(po, items, isFromCache))

    use notif = loader.Notifications.Subscribe handler
    loader.LoadProjects(existing) |> ignore // TODO: Maybe we should move away from event driven approach???

let internal loadInBackground onLoaded (loader: WorkspaceLoader) (projects: Project list) (generateBinlog: bool) =
    let (resProjects, otherProjects) = projects |> List.partition (fun n -> n.Response.IsSome)

    for project in resProjects do
        match project.Response with
        | Some res -> onLoaded (ProjectSystemState.Loaded(res.Options, res.ExtraInfo, res.Items, false))
        | None -> () //Shouldn't happen

    otherProjects |> List.map (fun n -> n.FileName) |> getProjectOptions loader onLoaded generateBinlog
