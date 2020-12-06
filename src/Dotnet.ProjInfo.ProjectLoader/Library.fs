namespace Dotnet.ProjInfo

open System
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework
open System.Runtime.Loader
open System.IO
open Microsoft.Build.Execution

module ProjectLoader =
    open Types
    open CommonHelpers

    type LoadedProject =
        private
        | LoadedProject of ProjectInstance

    type ProjectLoadingStatus =
        private
        | Success of LoadedProject
        | Error of string

    let init () =
        let instance = Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults()
        //Workaround from https://github.com/microsoft/MSBuildLocator/issues/86#issuecomment-640275377
        AssemblyLoadContext.Default.add_Resolving (fun assemblyLoadContext assemblyName ->
            let path = Path.Combine(instance.MSBuildPath, assemblyName.Name + ".dll")
            if File.Exists path then
                assemblyLoadContext.LoadFromAssemblyPath path
            else
                null
        )
        instance.MSBuildPath

    let logger (writer: StringWriter) = {
        new ILogger with
            member this.Initialize(eventSource: IEventSource): unit =
                eventSource.ErrorRaised.Add (fun t -> writer.WriteLine t.Message) //Only log errors

            member this.Parameters
                with get (): string =
                    ""
                and set (v: string): unit =
                    printfn "v"

            member this.Shutdown(): unit =
                ()

            member this.Verbosity
                with get (): LoggerVerbosity =
                    LoggerVerbosity.Detailed
                and set (v: LoggerVerbosity): unit =
                    ()
    }

    let loadProject (path: string) (toolsPath: string) =
        let globalProperties = dict [
            "SolutionDir", Path.GetDirectoryName path
            "MSBuildExtensionsPath", toolsPath
            "MSBuildSDKsPath", Path.Combine(toolsPath, "Sdks")
            "IsCrossTargetingBuild", "false" //Make sure we always target single TFM for Design Time Build
        ]

        use pc = new ProjectCollection(globalProperties)

        let pi = pc.LoadProject(path)
        let tfm =
            let tfm = pi.GetPropertyValue "TargetFramework"
            if String.IsNullOrWhiteSpace tfm then
                pi.GetPropertyValue "TargetFrameworks"
            else
                tfm

        let actualTFM = tfm.Split(';').[0] //Always parse targeting first defined TFM

        use sw = new StringWriter ()
        let logger = logger (sw)
        pi.SetGlobalProperty("SkipCompilerExecution", "true") |> ignore
        pi.SetGlobalProperty("ProvideCommandLineArgs", "true") |> ignore
        pi.SetGlobalProperty("CopyBuildOutputToOutputDirectory", "false") |> ignore
        pi.SetGlobalProperty("UseCommonOutputDirectory", "true") |> ignore
        pi.SetGlobalProperty("DesignTimeBuild", "true") |> ignore
        pi.SetGlobalProperty("BuildProjectReferences", "false") |> ignore
        pi.SetGlobalProperty("TargetFramework", actualTFM) |> ignore
        let pi = pi.CreateProjectInstance()
        let build = pi.Build("CoreCompile", [logger])
        if build then
            Success (LoadedProject pi)
        else
            Error (sw.ToString())

    let getFscArgs (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "FscCommandLineArgs")
        |> Seq.map (fun p -> p.EvaluatedInclude)

    let getP2Prefs (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "_MSBuildProjectReferenceExistent")
        |> Seq.map (fun p ->
            let relativePath = p.EvaluatedInclude
            let path = p.GetMetadataValue "FullPath"
            let tfms =
                if p.HasMetadata "TargetFramework" then
                    p.GetMetadataValue "TargetFramework"
                else
                    p.GetMetadataValue "TargetFrameworks"
            {RelativePath = relativePath; ProjectFileName = path; TargetFramework = tfms}
        )

    let getCompileItems (LoadedProject project) =
        project.Items
        |> Seq.filter (fun p -> p.ItemType = "Compile")
        |> Seq.map (fun p ->
            let name = p.EvaluatedInclude
            let link = if p.HasMetadata "Link" then Some (p.GetMetadataValue "Link") else None
            let fullPath = p.GetMetadataValue "FullPath"
            {Name = name; FullPath = fullPath; Link = link}
        )

    let getProperties (LoadedProject project) (properties: string list) =
        project.Properties
        |> Seq.filter (fun p -> List.contains p.Name properties )
        |> Seq.map (fun p -> { Name = p.Name; Value =  p.EvaluatedValue} )

    let getSdkInfo props =
        let (|ConditionEquals|_|) (str: string) (arg: string) =
            if System.String.Compare(str, arg, System.StringComparison.OrdinalIgnoreCase) = 0
            then Some() else None

        let (|StringList|_|) (str: string)  =
            str.Split([| ';' |], System.StringSplitOptions.RemoveEmptyEntries)
            |> List.ofArray
            |> Some

        let msbuildPropBool (s: Property) =
            match s.Value.Trim() with
            | "" -> None
            | ConditionEquals "True" -> Some true
            | _ -> Some false

        let msbuildPropStringList (s: Property) =
            match s.Value.Trim() with
            | "" -> []
            | StringList list  -> list
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

    let mapToProject (path: string) (fscArgs: string seq) (p2p: ProjectReference seq) (compile: CompileItem seq) (props: Property seq) =
        let projectSdk = getSdkInfo props
        let projDir = Path.GetDirectoryName path

        let fscArgsNormalized =
            //workaround, arguments in rsp can use relative paths
            fscArgs
            |> Seq.map (FscArguments.useFullPaths projDir)
            |> Seq.toList

        let sourceFiles, otherOptions =
            fscArgsNormalized
            |> List.partition (FscArguments.isSourceFile path)

        let compileItems =
            sourceFiles
            |> List.map (VisualTree.getCompileProjectItem (compile |> Seq.toList) path)

        let project =
            { ProjectId = Some path
              ProjectFileName = path
              TargetFramework = projectSdk.TargetFramework
              SourceFiles = sourceFiles
              OtherOptions = otherOptions
              ReferencedProjects = List.ofSeq p2p
              LoadTime = DateTime.Now
              TargetPath = props |> Seq.tryFind (fun n -> n.Name = "TargetPath") |> Option.map(fun n -> n.Value) |> Option.defaultValue ""
              ProjectOutputType =  FscArguments.outType fscArgsNormalized
              ProjectSdkInfo = projectSdk
              Items = compileItems }


        project


    let getProjectInfo(path: string) (toolsPath: string) : Result<Types.ProjectOptions, string> =
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

            let fscArgs = getFscArgs project
            let p2pRefs = getP2Prefs project
            let compileItems = getCompileItems project
            let props = getProperties project properties
            let proj = mapToProject path fscArgs p2pRefs compileItems props

            Result.Ok proj
        | Error e -> Result.Error e