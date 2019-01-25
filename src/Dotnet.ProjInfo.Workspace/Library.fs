namespace Dotnet.ProjInfo.Workspace
open System.Collections.Concurrent

type ProjectKey =
    { ProjectPath: string
      Configuration: string
      TargetFramework: string }

type Loader () =

    let event1 = new Event<_>()
    let parsedProjects = ConcurrentDictionary<_, _>()

    let getKey (po: ProjectOptions) =
        { ProjectKey.ProjectPath = po.ProjectFileName
          Configuration =
            match po.ExtraProjectInfo.ProjectSdkType with
            | ProjectSdkType.DotnetSdk t ->
                t.Configuration
            | ProjectSdkType.Verbose v ->
                "unknown"
          TargetFramework =
            match po.ExtraProjectInfo.ProjectSdkType with
            | ProjectSdkType.DotnetSdk t ->
                t.TargetFramework
            | ProjectSdkType.Verbose v ->
                "unknown"
        }

    [<CLIEvent>]
    member this.Event1 = event1.Publish

    member this.Projects = parsedProjects

    member x.LoadSln(sln: string) =
        ()

    member this.LoadProjects(projects: string list) =
        let cache = ProjectCrackerDotnetSdk.ParsedProjectCache()
        
        let notify arg =
            event1.Trigger(this, arg)

        for project in projects do
            match ProjectCrackerDotnetSdk.load notify cache project with
            | Ok (po, sources, props) ->
                let loaded = WorkspaceProjectState.Loaded (po, sources, props)
                parsedProjects.AddOrUpdate(getKey po, po, fun _ _ -> po) |> ignore
                notify loaded
            | Error e ->
                let failed = WorkspaceProjectState.Failed (project, e)
                notify failed

    member x.LoadFsx(fsx: string, tfm: string) =
        ()
