namespace Dotnet.ProjInfo.Workspace

open System.Collections.Concurrent
open ProjectRecognizer
open System.IO

type ProjectKey =
    { ProjectPath: string
      Configuration: string
      TargetFramework: string }

type Loader () =

    let event1 = new Event<_>()
    let parsedProjects = ConcurrentDictionary<_, _>()

    let mutable msbuildPath = Dotnet.ProjInfo.Inspect.MSBuildExePath.Path "msbuild"
    let mutable msbuildNetSdkPath = Dotnet.ProjInfo.Inspect.MSBuildExePath.DotnetMsbuild "dotnet"

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
                v.TargetFrameworkVersion |> Dotnet.ProjInfo.NETFramework.netifyTargetFrameworkVersion
        }

    [<CLIEvent>]
    member __.Notifications = event1.Publish

    member __.Projects
        with get () = parsedProjects.ToArray()

    member this.MSBuildPath
        with get () = msbuildPath
        and set (value) = msbuildPath <- value

    member this.MSBuildNetSdkPath
        with get () = msbuildNetSdkPath
        and set (value) = msbuildNetSdkPath <- value

    member x.LoadSln(sln: string) =
        ()

    member this.LoadProjects(projects: string list) =
        let cache = ProjectCrackerDotnetSdk.ParsedProjectCache()
        
        let notify arg =
            event1.Trigger(this, arg)

        for project in projects do

            let loader =
                if File.Exists project then
                    match project with
                    | NetCoreSdk ->
                        ProjectCrackerDotnetSdk.load this.MSBuildNetSdkPath
                    | Net45 ->
                        ProjectCrackerDotnetSdk.loadVerboseSdk this.MSBuildPath
                    | NetCoreProjectJson | Unsupported ->
                        failwithf "unsupported project %s" project
                 else
                    fun notify _ proj ->
                        let loading = WorkspaceProjectState.Loading (proj, [])
                        notify loading
                        Error (GetProjectOptionsErrors.GenericError(proj, "not found"))

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
