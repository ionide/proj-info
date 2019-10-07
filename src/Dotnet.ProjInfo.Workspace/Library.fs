namespace Dotnet.ProjInfo.Workspace

open System.Collections.Concurrent
open ProjectRecognizer
open System.IO

type ProjectKey =
    { ProjectPath: string
      TargetFramework: string }

type MSBuildLocator () =

    let installedMSBuilds = lazy (
        Dotnet.ProjInfo.Workspace.MSBuildInfo.installedMSBuilds () )

    member this.MSBuildFromPATH
        with get() = Dotnet.ProjInfo.Inspect.MSBuildExePath.Path "msbuild"

    member this.DotnetMSBuildFromPATH
        with get() =  Dotnet.ProjInfo.Inspect.MSBuildExePath.DotnetMsbuild "dotnet"

    member this.InstalledMSBuilds () =
        installedMSBuilds.Force()
        |> List.map (Dotnet.ProjInfo.Inspect.MSBuildExePath.Path)

    member this.LatestInstalledMSBuild () =
        match installedMSBuilds.Force() with
        | [] -> this.MSBuildFromPATH
        | path :: _ -> Dotnet.ProjInfo.Inspect.MSBuildExePath.Path path

type LoaderConfig = {
    MSBuildHost : Dotnet.ProjInfo.Inspect.MSBuildExePath
    MSBuildNetSdkHost: Dotnet.ProjInfo.Inspect.MSBuildExePath } with

    static member Default (msbuildLocator: MSBuildLocator) =
        let latestMSBuild = msbuildLocator.LatestInstalledMSBuild ()
        { LoaderConfig.MSBuildHost = latestMSBuild
          MSBuildNetSdkHost = msbuildLocator.DotnetMSBuildFromPATH }

    static member FromPATH (msbuildLocator: MSBuildLocator) =
        { LoaderConfig.MSBuildHost = msbuildLocator.MSBuildFromPATH
          MSBuildNetSdkHost = msbuildLocator.DotnetMSBuildFromPATH }

type Loader private (msbuildPath, msbuildNetSdkPath) =

    let event1 = new Event<_>()
    let parsedProjects = ConcurrentDictionary<_, _>()
    let lastProjectError = ConcurrentDictionary<_, _>()

    let getKey (po: ProjectOptions) =
        { ProjectKey.ProjectPath = po.ProjectFileName
          TargetFramework = po.TargetFramework }

    [<CLIEvent>]
    member __.Notifications = event1.Publish

    member __.Projects
        with get () = parsedProjects.ToArray()

    member __.LastError proj =
        match lastProjectError.TryGetValue(proj) with
        | true, v -> Some v
        | false, _ -> None

    member this.MSBuildPath
        with get () : Dotnet.ProjInfo.Inspect.MSBuildExePath = msbuildPath

    member this.MSBuildNetSdkPath
        with get () : Dotnet.ProjInfo.Inspect.MSBuildExePath = msbuildNetSdkPath

    member this.LoadProjects(projects: string list) =
        this.LoadProjects(projects, CrosstargetingStrategies.firstTargetFramework)

    member this.LoadProjects(projects: string list, crosstargetingStrategy: CrosstargetingStrategy) =

        let cache = ProjectCrackerDotnetSdk.ParsedProjectCache()
        
        let notify arg =
            event1.Trigger(this, arg)

        for project in projects do

            let loader =
                if File.Exists project then
                    match project with
                    | NetCoreSdk ->
                        ProjectCrackerDotnetSdk.load crosstargetingStrategy this.MSBuildNetSdkPath
                    | Net45 ->
                        ProjectCrackerDotnetSdk.loadVerboseSdk crosstargetingStrategy this.MSBuildPath
                    | NetCoreProjectJson | Unsupported ->
                        failwithf "unsupported project %s" project
                 else
                    fun notify _ proj ->
                        let loading = WorkspaceProjectState.Loading (proj, [])
                        notify loading
                        Error (GetProjectOptionsErrors.GenericError(proj, "not found"))

            match loader notify cache project with
            | Ok (po, props, additionalProjs) ->
                let rec visit (p: ProjectOptions) = seq {
                    yield p
                    for p2pRef in p.ReferencedProjects do
                        let p2p =
                            additionalProjs
                            |> List.find (fun p -> p.ProjectFileName = p2pRef.ProjectFileName && p.TargetFramework = p2pRef.TargetFramework)
                        yield! visit p2p }

                for proj in visit po do
                    parsedProjects.AddOrUpdate(getKey proj, proj, fun _ _ -> proj) |> ignore

                for proj in visit po do
                    WorkspaceProjectState.Loaded (proj, props)
                    |> notify

            | Error e ->
                let failed = WorkspaceProjectState.Failed (project, e)
                lastProjectError.AddOrUpdate(project, e, fun _ _ -> e) |> ignore
                notify failed

    member this.LoadSln(slnPath: string) =
        this.LoadSln(slnPath, CrosstargetingStrategies.firstTargetFramework)

    member this.LoadSln(slnPath: string, crosstargetingStrategy: CrosstargetingStrategy) =

        match InspectSln.tryParseSln slnPath with
        | Choice1Of2 (_, slnData) ->
            let projs = InspectSln.loadingBuildOrder slnData

            this.LoadProjects(projs, crosstargetingStrategy)
        | Choice2Of2 d ->
            failwithf "cannot load the sln: %A" d

    static member Create(config: LoaderConfig) =
        Loader(config.MSBuildHost, config.MSBuildNetSdkHost)

type NetFWInfoConfig = {
    MSBuildHost : Dotnet.ProjInfo.Inspect.MSBuildExePath } with

    static member Default (msbuildLocator: MSBuildLocator) =
        let latestMSBuild = msbuildLocator.LatestInstalledMSBuild ()
        { NetFWInfoConfig.MSBuildHost = latestMSBuild }

    static member FromPATH (msbuildLocator: MSBuildLocator) =
        { NetFWInfoConfig.MSBuildHost = msbuildLocator.MSBuildFromPATH }

type NetFWInfo private (msbuildPath) =

    let installedNETVersionsLazy = lazy (NETFrameworkInfoProvider.getInstalledNETVersions msbuildPath)

    let additionalArgsByTfm = System.Collections.Concurrent.ConcurrentDictionary<string, string list>()

    let additionalArgumentsBy targetFramework =
        let f tfm = NETFrameworkInfoProvider.getAdditionalArgumentsBy msbuildPath tfm
        additionalArgsByTfm.GetOrAdd(targetFramework, f)

    member this.MSBuildPath
        with get () : Dotnet.ProjInfo.Inspect.MSBuildExePath = msbuildPath

    member this.InstalledNetFws() =
        installedNETVersionsLazy.Force()

    member this.LatestVersion () =
        let maxByVersion list =
            //TODO extract and test
            list
            |> List.map (fun (s: string) -> s, (s.TrimStart('v').Replace(".","").PadRight(3, '0')))
            |> List.maxBy snd
            |> fst

        this.InstalledNetFws()
        |> maxByVersion

    member this.GetProjectOptionsFromScript(checkerGetProjectOptionsFromScript, targetFramework, file, source) =
        FSharpCompilerServiceChecker.getProjectOptionsFromScript additionalArgumentsBy checkerGetProjectOptionsFromScript file source targetFramework

    static member Create(config: NetFWInfoConfig) =
        NetFWInfo(config.MSBuildHost)

type ProjectViewerTree =
    { Name: string;
      Items: ProjectViewerItem list }
and [<RequireQualifiedAccess>] ProjectViewerItem =
    | Compile of string * ProjectViewerItemConfig
and ProjectViewerItemConfig =
    { Link: string }

type ProjectViewer () =

    member __.Render(proj: ProjectOptions) =

        let compileFiles =
            let sources = proj.Items
            match proj.ExtraProjectInfo.ProjectSdkType with
            | ProjectSdkType.Verbose _ ->
                //compatibility with old behaviour (projectcracker), so test output is exactly the same
                //the temp source files (like generated assemblyinfo.fs) are not added to sources
                let isTempFile (name: string) =
                    let tempPath = Path.GetTempPath()
                    let s = name.ToLower()
                    s.StartsWith(tempPath.ToLower())
                sources
                |> List.choose (function ProjectItem.Compile(name, fullpath) -> Some (name, fullpath))
                |> List.filter (fun (_,p) -> not(isTempFile p))
            | ProjectSdkType.DotnetSdk _ ->
                //the generated assemblyinfo.fs are not shown as sources
                let isGeneratedAssemblyinfo (name: string) =
                    let projName = proj.ProjectFileName |> Path.GetFileNameWithoutExtension
                    //TODO check is in `obj` dir for the tfm
                    //TODO better, get the name from fsproj
                    //TODO cs too
                    name.EndsWith(sprintf "%s.AssemblyInfo.fs" projName)
                sources
                |> List.choose (function ProjectItem.Compile(name, fullpath) -> Some (name, fullpath))
                |> List.filter (fun (_,p) -> not(isGeneratedAssemblyinfo p))

        { ProjectViewerTree.Name = proj.ProjectFileName |> Path.GetFileNameWithoutExtension
          Items = compileFiles
                  |> List.map(fun (name, fullpath) -> ProjectViewerItem.Compile(fullpath, { ProjectViewerItemConfig.Link = name })) }
