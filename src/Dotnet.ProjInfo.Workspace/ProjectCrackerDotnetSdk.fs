namespace Dotnet.ProjInfo.Workspace

open System
open System.IO
open Dotnet.ProjInfo.Inspect

module MSBuildPrj = Dotnet.ProjInfo.Inspect

exception ProjectInspectException of GetProjectOptionsErrors

module MSBuildKnownProperties =
    let TargetFramework = "TargetFramework"

module internal ProjectCrackerDotnetSdk =

  type NavigateProjectSM =
      | NoCrossTargeting of NoCrossTargetingData
      | CrossTargeting of string list
  and NoCrossTargetingData = { FscArgs: string list; P2PRefs: MSBuildPrj.ResolvedP2PRefsInfo list; Properties: Map<string,string>; Items: MSBuildPrj.GetItemResult list }

  open DotnetProjInfoInspectHelpers

  let msbuildPropProjectOutputType (s: string) =
    match s.Trim() with
    | MSBuildPrj.MSBuild.ConditionEquals "Exe" -> ProjectOutputType.Exe
    | MSBuildPrj.MSBuild.ConditionEquals "Library" -> ProjectOutputType.Library
    | x -> ProjectOutputType.Custom x

  let getExtraInfo props =
    let msbuildPropBool prop =
        props |> Map.tryFind prop |> Option.bind msbuildPropBool
    let msbuildPropStringList prop =
        props |> Map.tryFind prop |> Option.map msbuildPropStringList
    let msbuildPropString prop =
        props |> Map.tryFind prop

    { ProjectSdkTypeDotnetSdk.IsTestProject = msbuildPropBool "IsTestProject" |> Option.defaultValue false
      Configuration = msbuildPropString "Configuration" |> Option.defaultValue ""
      IsPackable = msbuildPropBool "IsPackable" |> Option.defaultValue false
      TargetFramework = msbuildPropString MSBuildKnownProperties.TargetFramework |> Option.defaultValue ""
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

  let getExtraInfoVerboseSdk props =
    let msbuildPropBool prop =
        props |> Map.tryFind prop |> Option.bind msbuildPropBool
    let msbuildPropStringList prop =
        props |> Map.tryFind prop |> Option.map msbuildPropStringList
    let msbuildPropString prop =
        props |> Map.tryFind prop

    { Configuration = msbuildPropString "Configuration" |> Option.defaultValue ""
      TargetFrameworkVersion = msbuildPropString "TargetFrameworkVersion" |> Option.defaultValue "" }

  type private ProjectParsingSdk = DotnetSdk | VerboseSdk

  type ParsedProject = string * ProjectOptions * ((string * string) list) * (ProjectOptions list)
  type ParsedProjectCache = Collections.Concurrent.ConcurrentDictionary<string, ParsedProject>

  let private execProjInfoFromMsbuild msbuildPath notifyState parseAsSdk additionalMSBuildProps file =
    let projDir = Path.GetDirectoryName file

    notifyState (WorkspaceProjectState.Loading (file, additionalMSBuildProps))

    match parseAsSdk with
    | ProjectParsingSdk.DotnetSdk ->
        let projectAssetsJsonPath = Path.Combine(projDir, "obj", "project.assets.json")
        if not(File.Exists(projectAssetsJsonPath)) then
            raise (ProjectInspectException (ProjectNotRestored file))
    | ProjectParsingSdk.VerboseSdk ->
        ()

    let getFscArgs =
        match parseAsSdk with
        | ProjectParsingSdk.DotnetSdk ->
            Dotnet.ProjInfo.Inspect.getFscArgs
        | ProjectParsingSdk.VerboseSdk ->
            let asFscArgs props =
                let fsc = Microsoft.FSharp.Build.Fsc()
                Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
            Dotnet.ProjInfo.Inspect.getFscArgsOldSdk (asFscArgs >> Ok)

    let getP2PRefs = Dotnet.ProjInfo.Inspect.getResolvedP2PRefs
    let additionalInfo = //needed for extra
        [ "OutputType"
          "IsTestProject"
          "TargetPath"
          "Configuration"
          "IsPackable"
          MSBuildKnownProperties.TargetFramework
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
        ]
    let gp () = Dotnet.ProjInfo.Inspect.getProperties (["TargetPath"; "IsCrossTargetingBuild"; "TargetFrameworks"] @ additionalInfo)

    let getItems () = Dotnet.ProjInfo.Inspect.getItems [("Compile", GetItemsModifier.FullPath); ("Compile", GetItemsModifier.Custom("Link"))] []

    let loggedMessages = System.Collections.Concurrent.ConcurrentQueue<string>()

    let runCmd exePath args = Utils.runProcess loggedMessages.Enqueue projDir exePath (args |> String.concat " ")

    let msbuildExec =
        Dotnet.ProjInfo.Inspect.msbuild msbuildPath runCmd

    let additionalArgs = additionalMSBuildProps |> List.map (Dotnet.ProjInfo.Inspect.MSBuild.MSbuildCli.Property)

    let inspect =
        match parseAsSdk with
        | ProjectParsingSdk.DotnetSdk ->
            Dotnet.ProjInfo.Inspect.getProjectInfos
        | ProjectParsingSdk.VerboseSdk ->
            Dotnet.ProjInfo.Inspect.getProjectInfos // getProjectInfosOldSdk

    let globalArgs =
        match Environment.GetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL") with
        | "1" -> Dotnet.ProjInfo.Inspect.MSBuild.MSbuildCli.Switch("bl") :: []
        | _ -> []

    let infoResult =
        file
        |> inspect loggedMessages.Enqueue msbuildExec [getFscArgs; getP2PRefs; gp; getItems] (additionalArgs @ globalArgs)

    infoResult, (loggedMessages.ToArray() |> Array.toList)


  let mapMSBuildResults (results, log) =
    match results with
    | MsbuildOk [getFscArgsResult; getP2PRefsResult; gpResult; gpItemResult] ->
        match getFscArgsResult, getP2PRefsResult, gpResult, gpItemResult with
        | MsbuildError(MSBuildPrj.MSBuildSkippedTarget), MsbuildError(MSBuildPrj.MSBuildSkippedTarget), MsbuildOk (MSBuildPrj.GetResult.Properties props), _ ->
            // Projects with multiple target frameworks, fails if the target framework is not choosen
            let prop key = props |> Map.ofList |> Map.tryFind key

            match prop "IsCrossTargetingBuild", prop "TargetFrameworks" with
            | Some (MSBuildPrj.MSBuild.ConditionEquals "true"), Some (MSBuildPrj.MSBuild.StringList tfms) ->
                CrossTargeting tfms
            | _ ->
                failwithf "error getting msbuild info: some targets skipped, found props: %A" props
        | MsbuildOk (MSBuildPrj.GetResult.FscArgs fa), MsbuildOk (MSBuildPrj.GetResult.ResolvedP2PRefs p2p), MsbuildOk (MSBuildPrj.GetResult.Properties p), MsbuildOk (MSBuildPrj.GetResult.Items pi) ->
            NoCrossTargeting { FscArgs = fa; P2PRefs = p2p; Properties = p |> Map.ofList; Items = pi }
        | r ->
            failwithf "error getting msbuild info: %A" r
    | MsbuildOk r ->
        failwithf "error getting msbuild info: internal error, more info returned than expected %A" r
    | MsbuildError r ->
        match r with
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.MSBuildSkippedTarget ->
            failwithf "Unexpected MSBuild result, all targets skipped"
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.UnexpectedMSBuildResult(r) ->
            failwithf "Unexpected MSBuild result %s" r
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.MSBuildFailed(exitCode, (workDir, exePath, args)) ->
            let logMsg = [ yield "Log: "; yield! log ] |> String.concat (Environment.NewLine)
            let msbuildErrorMsg =
                [ sprintf "MSBuild failed with exitCode %i" exitCode
                  sprintf "Working Directory: '%s'" workDir
                  sprintf "Exe Path: '%s'" exePath
                  sprintf "Args: '%s'" args ]
                |> String.concat " "

            failwithf "%s%s%s" msbuildErrorMsg (Environment.NewLine) logMsg
    | _ ->
        failwithf "error getting msbuild info: internal error"

  let asProjInfoCached (cache: ParsedProjectCache) getProjInfoOf additionalMSBuildProps file : ParsedProject =
    let key = sprintf "%s;%A" file additionalMSBuildProps
    match cache.TryGetValue(key) with
    | true, alreadyParsed ->
        alreadyParsed
    | false, _ ->
        let p = file |> getProjInfoOf additionalMSBuildProps
        cache.AddOrUpdate(key, p, fun _ _ -> p)


  let private visitSingleTfmProj follow parseAsSdk (file, projData) =

    let { NoCrossTargetingData.FscArgs = rsp; P2PRefs = p2ps; Properties = props; Items = projItems } = projData

    let projDir = Path.GetDirectoryName file

    //TODO cache projects info of p2p ref
    let p2pProjects : ParsedProject list =
        p2ps
        // TODO before was no follow. now follow other projects too
        // do not follow others lang project, is not supported by FCS anyway
        // |> List.filter (fun p2p -> p2p.ProjectReferenceFullPath.ToLower().EndsWith(".fsproj"))
        |> List.map (fun p2p ->
            let followP2pArgs =
                p2p.TargetFramework
                |> Option.map (fun tfm -> MSBuildKnownProperties.TargetFramework, tfm)
                |> Option.toList
            p2p.ProjectReferenceFullPath |> follow followP2pArgs )

    let tar =
        match props |> Map.tryFind "TargetPath" with
        | Some t -> t
        | None -> failwith "error, 'TargetPath' property not found"

    let rspNormalized =
        //workaround, arguments in rsp can use relative paths
        rsp |> List.map (FscArguments.useFullPaths projDir)

    let sdkTypeData, log =
        match parseAsSdk with
        | ProjectParsingSdk.DotnetSdk ->
            let extraInfo = getExtraInfo props
            ProjectSdkType.DotnetSdk(extraInfo), []
        | ProjectParsingSdk.VerboseSdk ->
            //compatibility with old behaviour, so output is exactly the same
            let mergedLog =
                [ yield (file, "")
                  yield! p2pProjects |> List.collect (fun (_,_,x,_) -> x) ]
            
            let extraInfo = getExtraInfoVerboseSdk props
            ProjectSdkType.Verbose(extraInfo), mergedLog

    let isSourceFile : (string -> bool) =
        if Path.GetExtension(file) = ".fsproj" then
            FscArguments.isCompileFile
        else
            (fun n -> n.EndsWith ".cs")

    let sourceFiles, otherOptions =
        rspNormalized
        |> List.partition isSourceFile

    let compileItems =
        sourceFiles
        |> List.map (VisualTree.getCompileProjectItem projItems file)

    let po =
        {
            ProjectId = Some file
            ProjectFileName = file
            TargetFramework = 
                match sdkTypeData with
                | ProjectSdkType.DotnetSdk t ->
                    t.TargetFramework
                | ProjectSdkType.Verbose v ->
                    v.TargetFrameworkVersion |> Dotnet.ProjInfo.NETFramework.netifyTargetFrameworkVersion
            SourceFiles = sourceFiles
            OtherOptions = otherOptions
            ReferencedProjects =
                p2pProjects
                |> List.map (fun (_,y,_,_) -> { ProjectReference.ProjectFileName = y.ProjectFileName; TargetFramework = y.TargetFramework })
            LoadTime = DateTime.Now
            Items = compileItems
            ExtraProjectInfo =
                {
                    TargetPath = tar
                    ExtraProjectInfoData.ProjectSdkType = sdkTypeData
                    ExtraProjectInfoData.ProjectOutputType = FscArguments.outType rspNormalized
                }
        }

    let additionalProjects : ProjectOptions list =
        let rec getAdditionalProjs (_,parsedP2P,_,parsedP2PDeps) =
            [ yield parsedP2P
              yield! parsedP2PDeps ]

        p2pProjects |> List.collect getAdditionalProjs

    (tar, po, log, additionalProjects)

  let rec private projInfoOf projInfoFromMsbuild projInfoCached parseAsSdk additionalMSBuildProps file : ParsedProject =

    let follow = projInfoCached (projInfoOf projInfoFromMsbuild projInfoCached parseAsSdk)

    let todo =
        projInfoFromMsbuild parseAsSdk additionalMSBuildProps file
        |> mapMSBuildResults

    match todo with
    | CrossTargeting tfms ->
        failwithf "Unexpected, found cross targeting %A but expecting a single tfm for props %A" tfms additionalMSBuildProps
    | NoCrossTargeting noCrossTargetingData ->
        visitSingleTfmProj follow parseAsSdk (file, noCrossTargetingData)

  let private projInfoCrossTargeting crosstargetingStrategy projInfoFromMsbuild projInfoCached parseAsSdk additionalMSBuildProps file : ParsedProject =

    let follow = projInfoCached (projInfoOf projInfoFromMsbuild projInfoCached parseAsSdk)

    let todo =
        projInfoFromMsbuild parseAsSdk additionalMSBuildProps file
        |> mapMSBuildResults

    match todo with
    | CrossTargeting [] ->
        failwithf "Unexpected, found cross targeting but empty target frameworks list"
    | CrossTargeting [tfm] ->
        // single tfm in <TargetFrameworks>, maybe log because is strange, should just be <TargetFramework>
        follow [MSBuildKnownProperties.TargetFramework, tfm] file
    | CrossTargeting (firstTfm :: secondTfm :: othersTfms) ->
        let tfm = crosstargetingStrategy file (firstTfm, secondTfm, othersTfms)
        //TODO check tfm is contained in tfms
        follow [MSBuildKnownProperties.TargetFramework, tfm] file
    | NoCrossTargeting noCrossTargetingData ->
        visitSingleTfmProj follow parseAsSdk (file, noCrossTargetingData)


  let private getProjectOptionsFromProjectFile crosstargetingChooser projInfoFromMsbuild projInfoCached parseAsSdk (rootProjFile : string) =

    let _, po, log, additionalProjs = projInfoCached (projInfoCrossTargeting crosstargetingChooser projInfoFromMsbuild projInfoCached parseAsSdk) [] rootProjFile
    (po, log, additionalProjs)

  let private loadBySdk crosstargetingStrategy msbuildPath notifyState projInfoCached parseAsSdk file =
      try
        let po, log, additionalProjs = getProjectOptionsFromProjectFile crosstargetingStrategy (execProjInfoFromMsbuild msbuildPath notifyState) projInfoCached parseAsSdk file

        Ok (po, (log |> Map.ofList), additionalProjs)
      with
        | ProjectInspectException d -> Error d
        | e -> Error (GenericError(file, e.Message))

  let load crosstargetingStrategy msbuildPath notifyState (cache: ParsedProjectCache) file =
      let projInfoCached = asProjInfoCached cache
      loadBySdk crosstargetingStrategy msbuildPath notifyState projInfoCached ProjectParsingSdk.DotnetSdk file

  let loadVerboseSdk crosstargetingStrategy msbuildPath notifyState (cache: ParsedProjectCache) file =
      let projInfoCached = asProjInfoCached cache
      loadBySdk crosstargetingStrategy msbuildPath notifyState projInfoCached ProjectParsingSdk.VerboseSdk file

type CrosstargetingStrategy = string -> (string * string * string list) -> string

module CrosstargetingStrategies =

    let firstTargetFramework (_file: string) (firstTfm: string, _secondTfm: string, _othersTfms: string list) =
      firstTfm

    let preferDotnetCore (_file: string) (firstTfm: string, secondTfm: string, othersTfms: string list) =
      let tfms =
        (firstTfm :: secondTfm :: othersTfms)
        |> List.sortByDescending id // make recent versions first, if same tfm id

      match tfms |> List.tryFind (fun s -> s.StartsWith("netcoreapp")) with
      | Some netcoreappTfm -> netcoreappTfm
      | None ->
          match tfms |> List.tryFind (fun s -> s.StartsWith("netstandard")) with
          | Some netstandardTfm -> netstandardTfm
          | None ->
                firstTfm
