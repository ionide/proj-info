namespace Dotnet.ProjInfo.Workspace

open System.Collections.Concurrent
open ProjectRecognizer

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
                v.Configuration
          TargetFramework =
            match po.ExtraProjectInfo.ProjectSdkType with
            | ProjectSdkType.DotnetSdk t ->
                t.TargetFramework
            | ProjectSdkType.Verbose v ->
                v.TargetFrameworkVersion
        }

    [<CLIEvent>]
    member __.Event1 = event1.Publish

    member __.Projects
        with get () = parsedProjects.ToArray()

    member x.LoadSln(sln: string) =
        ()

    member this.LoadProjects(projects: string list) =
        let cache = ProjectCrackerDotnetSdk.ParsedProjectCache()
        
        let notify arg =
            event1.Trigger(this, arg)

        for project in projects do

            let loader =
                match project with
                | NetCoreSdk ->
                    ProjectCrackerDotnetSdk.load
                | Net45 ->
                    ProjectCrackerDotnetSdk.loadVerboseSdk
                | NetCoreProjectJson | Unsupported ->
                    failwithf "unsupported project %s" project

            match loader notify cache project with
            | Ok (po, sources, props) ->
                let loaded = WorkspaceProjectState.Loaded (po, sources, props)

                let rec visit (p: ProjectOptions) = seq {
                    yield p
                    for (_, p2p) in p.ReferencedProjects do
                        yield! visit p2p }

                for proj in visit po do
                    parsedProjects.AddOrUpdate(getKey proj, proj, fun _ _ -> proj) |> ignore
                notify loaded
            | Error e ->
                let failed = WorkspaceProjectState.Failed (project, e)
                notify failed

    member x.LoadFsx(fsx: string, tfm: string) =
        ()
