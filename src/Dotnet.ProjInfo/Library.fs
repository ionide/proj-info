namespace Dotnet.ProjInfo

open System
open System.Collections.Generic
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework
open System.Runtime.Loader
open System.IO
open Microsoft.Build.Execution
open Types

[<RequireQualifiedAccess>]
module Init =
    ///Initialize the MsBuild integration. Returns path to MsBuild tool that was detected by Locator. Needs to be called before doing anything else
    let init () =
        let instance = Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults()
        //Workaround from https://github.com/microsoft/MSBuildLocator/issues/86#issuecomment-640275377
        AssemblyLoadContext.Default.add_Resolving
            (fun assemblyLoadContext assemblyName ->
                let path = Path.Combine(instance.MSBuildPath, assemblyName.Name + ".dll")

                if File.Exists path
                then assemblyLoadContext.LoadFromAssemblyPath path
                else null)

        ToolsPath instance.MSBuildPath

///Low level APIs for single project loading. Doesn't provide caching, and doesn't follow p2p references.
/// In most cases you want to use `Dotnet.ProjInf.WorkspaceLoader` type instead
module ProjectLoader =

    type LoadedProject = private LoadedProject of ProjectInstance

    type ProjectLoadingStatus =
        private
        | Success of LoadedProject
        | Error of string

    let internal logger (writer: StringWriter) =
        { new ILogger with
            member this.Initialize(eventSource: IEventSource): unit =
                // eventSource.ErrorRaised.Add(fun t -> writer.WriteLine t.Message) //Only log errors
                eventSource.AnyEventRaised.Add(fun t -> writer.WriteLine t.Message)

            member this.Parameters: string = ""

            member this.Parameters
                with set (v: string): unit = printfn "v"

            member this.Shutdown(): unit = ()
            member this.Verbosity: LoggerVerbosity = LoggerVerbosity.Detailed

            member this.Verbosity
                with set (v: LoggerVerbosity): unit = () }

    let getTfm (path: string) =
        let pi = ProjectInstance(path)
        let tfm = pi.GetPropertyValue "TargetFramework"

        if String.IsNullOrWhiteSpace tfm then
            let tfms = pi.GetPropertyValue "TargetFrameworks"
            let actualTFM = tfms.Split(';').[0]
            Some actualTFM
        else
            None

    let loadProject (path: string) (ToolsPath toolsPath) =
        try
            let tfm = getTfm path


            let globalProperties =
                dict [ "ProvideCommandLineArgs", "true"
                       "DesignTimeBuild", "true"
                       "SkipCompilerExecution", "true"
                       "GeneratePackageOnBuild", "false"
                       "Configuration", "Debug"
                       "DefineExplicitDefaults", "true"
                       "BuildProjectReferences", "false"
                       "UseCommonOutputDirectory", "false"
                       "DotnetProjInfo", "true"
                       if tfm.IsSome
                       then "TargetFramework", tfm.Value ]

            use pc = new ProjectCollection(globalProperties)

            let pi = pc.LoadProject(path)

            use sw = new StringWriter()
            let logger = logger (sw)

            let pi = pi.CreateProjectInstance()

            let build =
                pi.Build(
                    [| "ResolvePackageDependenciesDesignTime"
                       "_GenerateCompileDependencyCache"
                       "CoreCompile" |],
                    [ logger ]
                )

            if build
            then Success(LoadedProject pi)
            else Error(sw.ToString())
        with exc -> Error(exc.Message)

    let getFscArgs (LoadedProject project) =
        project.Items |> Seq.filter (fun p -> p.ItemType = "FscCommandLineArgs") |> Seq.map (fun p -> p.EvaluatedInclude)

    let getP2Prefs (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "_MSBuildProjectReferenceExistent")
        |> Seq.map
            (fun p ->
                let relativePath = p.EvaluatedInclude
                let path = p.GetMetadataValue "FullPath"

                let tfms =
                    if p.HasMetadata "TargetFramework"
                    then p.GetMetadataValue "TargetFramework"
                    else p.GetMetadataValue "TargetFrameworks"

                { RelativePath = relativePath
                  ProjectFileName = path
                  TargetFramework = tfms })

    let getCompileItems (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "Compile")
        |> Seq.map
            (fun p ->
                let name = p.EvaluatedInclude

                let link =
                    if p.HasMetadata "Link"
                    then Some(p.GetMetadataValue "Link")
                    else None

                let fullPath = p.GetMetadataValue "FullPath"

                { Name = name
                  FullPath = fullPath
                  Link = link })

    let getNuGetReferences (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "Reference" && p.GetMetadataValue "NuGetSourceType" = "Package")
        |> Seq.map
            (fun p ->
                let name = p.GetMetadataValue "NuGetPackageId"
                let version = p.GetMetadataValue "NuGetPackageVersion"
                let fullPath = p.GetMetadataValue "FullPath"

                { Name = name
                  Version = version
                  FullPath = fullPath })

    let getProperties (LoadedProject project) (properties: string list) =
        project.Properties
        |> Seq.filter (fun p -> List.contains p.Name properties)
        |> Seq.map
            (fun p ->
                { Name = p.Name
                  Value = p.EvaluatedValue })

    let getSdkInfo (props: Property seq) =
        let (|ConditionEquals|_|) (str: string) (arg: string) =
            if System.String.Compare(str, arg, System.StringComparison.OrdinalIgnoreCase) = 0
            then Some()
            else None

        let (|StringList|_|) (str: string) =
            str.Split([| ';' |], System.StringSplitOptions.RemoveEmptyEntries) |> List.ofArray |> Some

        let msbuildPropBool (s: Property) =
            match s.Value.Trim() with
            | "" -> None
            | ConditionEquals "True" -> Some true
            | _ -> Some false

        let msbuildPropStringList (s: Property) =
            match s.Value.Trim() with
            | "" -> []
            | StringList list -> list
            | _ -> []

        let msbuildPropBool (prop) =
            props |> Seq.tryFind (fun n -> n.Name = prop) |> Option.bind msbuildPropBool

        let msbuildPropStringList prop =
            props |> Seq.tryFind (fun n -> n.Name = prop) |> Option.map msbuildPropStringList

        let msbuildPropString prop =
            props |> Seq.tryFind (fun n -> n.Name = prop) |> Option.map (fun n -> n.Value.Trim())

        { IsTestProject = msbuildPropBool "IsTestProject" |> Option.defaultValue false
          Configuration = msbuildPropString "Configuration" |> Option.defaultValue ""
          IsPackable = msbuildPropBool "IsPackable" |> Option.defaultValue false
          TargetFramework = msbuildPropString "TargetFramework" |> Option.defaultValue ""
          TargetFrameworkIdentifier = msbuildPropString "TargetFrameworkIdentifier" |> Option.defaultValue ""
          TargetFrameworkVersion = msbuildPropString "TargetFrameworkVersion" |> Option.defaultValue ""

          MSBuildAllProjects = msbuildPropStringList "MSBuildAllProjects" |> Option.defaultValue []
          MSBuildToolsVersion = msbuildPropString "MSBuildToolsVersion" |> Option.defaultValue ""

          ProjectAssetsFile = msbuildPropString "ProjectAssetsFile" |> Option.defaultValue ""
          RestoreSuccess = msbuildPropBool "RestoreSuccess" |> Option.defaultValue false

          Configurations = msbuildPropStringList "Configurations" |> Option.defaultValue []
          TargetFrameworks = msbuildPropStringList "TargetFrameworks" |> Option.defaultValue []

          RunArguments = msbuildPropString "RunArguments"
          RunCommand = msbuildPropString "RunCommand"

          IsPublishable = msbuildPropBool "IsPublishable" }

    let mapToProject (path: string) (fscArgs: string seq) (p2p: ProjectReference seq) (compile: CompileItem seq) (nugetRefs: PackageReference seq) (sdkInfo: ProjectSdkInfo) (props: Property seq) (customProps: Property seq) =
        let projDir = Path.GetDirectoryName path

        let fscArgsNormalized =
            //workaround, arguments in rsp can use relative paths
            fscArgs |> Seq.map (FscArguments.useFullPaths projDir) |> Seq.toList

        let sourceFiles, otherOptions = fscArgsNormalized |> List.partition (FscArguments.isSourceFile path)

        let compileItems = sourceFiles |> List.map (VisualTree.getCompileProjectItem (compile |> Seq.toList) path)

        let project =
            { ProjectId = Some path
              ProjectFileName = path
              TargetFramework = sdkInfo.TargetFramework
              SourceFiles = sourceFiles
              OtherOptions = otherOptions
              ReferencedProjects = List.ofSeq p2p
              PackageReferences = List.ofSeq nugetRefs
              LoadTime = DateTime.Now
              TargetPath = props |> Seq.tryFind (fun n -> n.Name = "TargetPath") |> Option.map (fun n -> n.Value) |> Option.defaultValue ""
              ProjectOutputType = FscArguments.outType fscArgsNormalized
              ProjectSdkInfo = sdkInfo
              Items = compileItems
              CustomProperties = List.ofSeq customProps }


        project


    /// <summary>
    /// Main entry point for project loading.
    /// </summary>
    /// <param name="path">Full path to the `.fsproj` file</param>
    /// <param name="toolsPath">Path to MsBuild obtained from `ProjectLoader.init ()`</param>
    /// <param name="customProperties">List of additional MsBuild properties that you want to obtain.</param>
    /// <returns>Returns the record instance representing the loaded project or string containing error message</returns>
    let getProjectInfo (path: string) (toolsPath: ToolsPath) (customProperties: string list): Result<Types.ProjectOptions, string> =
        let loadedProject = loadProject path toolsPath

        match loadedProject with
        | Success project ->
            let properties =
                [ "OutputType"
                  "IsTestProject"
                  "TargetPath"
                  "Configuration"
                  "IsPackable"
                  "TargetFramework"
                  "TargetFrameworkIdentifier"
                  "TargetFrameworkVersion"
                  "MSBuildAllProjects"
                  "ProjectAssetsFile"
                  "RestoreSuccess"
                  "Configurations"
                  "TargetFrameworks"
                  "RunArguments"
                  "RunCommand"
                  "IsPublishable"
                  "BaseIntermediateOutputPath"
                  "TargetPath"
                  "IsCrossTargetingBuild"
                  "TargetFrameworks" ]

            let p2pRefs = getP2Prefs project

            let fscArgs = getFscArgs project
            // |> Seq.map
            //     (fun n -> //Hack beacuse FCSArgs contain bin/Debug paths without traget framework. No idea why.
            //         if n.StartsWith "-r:" then n else n)

            let compileItems = getCompileItems project
            let nuGetRefs = getNuGetReferences project
            let props = getProperties project properties
            let sdkInfo = getSdkInfo props
            let customProps = getProperties project customProperties

            if not sdkInfo.RestoreSuccess then
                Result.Error "not restored"
            else

                let proj = mapToProject path fscArgs p2pRefs compileItems nuGetRefs sdkInfo props customProps

                Result.Ok proj
        | Error e -> Result.Error e

type WorkspaceLoader private (toolsPath: ToolsPath) =
    let loadingNotification = new Event<Types.WorkspaceProjectState>()

    [<CLIEvent>]
    member __.Notifications = loadingNotification.Publish

    member __.LoadProjects(projects: string list, customProperties: string list) =
        let cache = Dictionary<string, ProjectOptions>()

        let getAllKnonw () =
            cache |> Seq.map (fun n -> n.Value) |> Seq.toList

        let rec loadProjectList (projectList: string list) =
            for p in projectList do
                let newList, toTrigger =
                    if cache.ContainsKey p then
                        let project = cache.[p]
                        loadingNotification.Trigger(WorkspaceProjectState.Loaded(project, getAllKnonw (), true)) //TODO: Should it even notify here?
                        let lst = project.ReferencedProjects |> Seq.map (fun n -> n.ProjectFileName) |> Seq.toList
                        lst, None
                    else
                        loadingNotification.Trigger(WorkspaceProjectState.Loading p)
                        let res = ProjectLoader.getProjectInfo p toolsPath customProperties

                        match res with
                        | Ok project ->
                            try
                                cache.Add(p, project)
                                let lst = project.ReferencedProjects |> Seq.map (fun n -> n.ProjectFileName) |> Seq.toList
                                let info = Some project
                                lst, info
                            with exc ->
                                loadingNotification.Trigger(WorkspaceProjectState.Failed(p, GenericError(p, exc.Message)))
                                [], None
                        | Error msg when msg.Contains "The project file could not be loaded." ->
                            loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotFound(p)))
                            [], None
                        | Error msg ->
                            loadingNotification.Trigger(WorkspaceProjectState.Failed(p, GenericError(p, msg)))
                            [], None

                loadProjectList newList

                toTrigger
                |> Option.iter (fun project -> loadingNotification.Trigger(WorkspaceProjectState.Loaded(project, getAllKnonw (), false)))

        loadProjectList projects
        cache |> Seq.map (fun n -> n.Value)

    member this.LoadProjects(projects) = this.LoadProjects(projects, [])

    member this.LoadProject(project, customProperties: string list) =
        this.LoadProjects([ project ], customProperties)

    member this.LoadProject(project) = this.LoadProjects([ project ])

    member this.LoadSln(sln, customProperties: string list) =
        match InspectSln.tryParseSln sln with
        | Ok (_, slnData) ->
            let projs = InspectSln.loadingBuildOrder slnData
            this.LoadProjects(projs, customProperties)
        | Error d -> failwithf "Cannot load the sln: %A" d

    member this.LoadSln(sln) = this.LoadSln(sln, [])

    static member Create(toolsPath: ToolsPath) = WorkspaceLoader(toolsPath)

type ProjectViewerTree =
    { Name: string
      Items: ProjectViewerItem list }

and [<RequireQualifiedAccess>] ProjectViewerItem = Compile of string * ProjectViewerItemConfig

and ProjectViewerItemConfig = { Link: string }

module ProjectViewer =

    let render (proj: ProjectOptions) =

        let compileFiles =
            let sources = proj.Items

            //the generated assemblyinfo.fs are not shown as sources
            let isGeneratedAssemblyinfo (name: string) =
                let projName = proj.ProjectFileName |> Path.GetFileNameWithoutExtension
                //TODO check is in `obj` dir for the tfm
                //TODO better, get the name from fsproj
                //TODO cs too
                name.EndsWith(sprintf "%s.AssemblyInfo.fs" projName)

            sources
            |> List.choose
                (function
                | ProjectItem.Compile (name, fullpath) -> Some(name, fullpath))
            |> List.filter (fun (_, p) -> not (isGeneratedAssemblyinfo p))

        { ProjectViewerTree.Name = proj.ProjectFileName |> Path.GetFileNameWithoutExtension
          Items =
              compileFiles
              |> List.map (fun (name, fullpath) -> ProjectViewerItem.Compile(fullpath, { ProjectViewerItemConfig.Link = name })) }
