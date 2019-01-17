namespace Dotnet.ProjInfo.Workspace

type Loader () =

    let event1 = new Event<_>()

    [<CLIEvent>]
    member this.Event1 = event1.Publish

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
                notify loaded
            | Error e ->
                let failed = WorkspaceProjectState.Failed (project, e)
                notify failed

    member x.LoadFsx(fsx: string, tfm: string) =
        ()
