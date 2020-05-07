namespace Dotnet.ProjInfo.Workspace

open System
open System.IO
open Dotnet.ProjInfo
open System.Collections.Concurrent
open DotnetProjInfoInspectHelpers

module internal ProjectCrackerSln =

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

  let private mapProjResults (file: string) projData =
    let (rsp, p2ps, props, projItems) = projData




    // //TODO cache projects info of p2p ref
    // let p2pProjects : ParsedProject list =
    //     p2ps
    //     // TODO before was no follow. now follow other projects too
    //     // do not follow others lang project, is not supported by FCS anyway
    //     // |> List.filter (fun p2p -> p2p.ProjectReferenceFullPath.ToLower().EndsWith(".fsproj"))
    //     |> List.choose (fun p2p ->
    //         let followP2pArgs =
    //             p2p.TargetFramework
    //             |> Option.map (fun tfm -> MSBuildKnownProperties.TargetFramework, tfm)
    //             |> Option.toList
    //         p2p.ProjectReferenceFullPath |> follow followP2pArgs )

    let file =
      match props |> Map.tryFind "MSBuildProjectFullPath" with
        | Some t -> t
        | None -> failwith "error, 'MSBuildProjectFullPath' property not found"

    let projDir = Path.GetDirectoryName file

    let tar =
        match props |> Map.tryFind "TargetPath" with
        | Some t -> t
        | None -> failwith "error, 'TargetPath' property not found"

    let rspNormalized =
        //workaround, arguments in rsp can use relative paths
        rsp |> List.map (FscArguments.useFullPaths projDir)

    let sdkTypeData =
      let extraInfo = getExtraInfo props
      ProjectSdkType.DotnetSdk(extraInfo)

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
            ReferencedProjects = p2ps |> List.map (fun (y: Inspect.ResolvedP2PRefsInfo) -> { ProjectReference.ProjectFileName = y.ProjectReferenceFullPath; TargetFramework = (defaultArg y.TargetFramework "") })
            LoadTime = DateTime.Now
            Items = compileItems
            ExtraProjectInfo =
                {
                    TargetPath = tar
                    ExtraProjectInfoData.ProjectSdkType = sdkTypeData
                    ExtraProjectInfoData.ProjectOutputType = FscArguments.outType rspNormalized
                }
        }

    (tar, po)

  let mapParseResults result =
    match result with
    | [getFscArgsResult; getP2PRefsResult; gpResult; gpItemResult] ->
        match getFscArgsResult, getP2PRefsResult, gpResult, gpItemResult with
        | MsbuildOk (Inspect.GetResult.FscArgs fa), MsbuildOk (Inspect.GetResult.ResolvedP2PRefs p2p), MsbuildOk (Inspect.GetResult.Properties p), MsbuildOk (Inspect.GetResult.Items pi) ->
            Ok (fa, p2p, p |> Map.ofList, pi)
        | r ->
            Error (sprintf "error getting msbuild info: %A" r)
    | r ->
      Error (sprintf "error getting msbuild info: internal error, more info returned than expected %A" r)

  let mapMSBuildResults (results, log) =
    match results with
    | MsbuildOk _ -> Ok ()
    | MsbuildError r ->
        match r with
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.MSBuildSkippedTarget ->
            Error "Unexpected MSBuild result, all targets skipped"
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.UnexpectedMSBuildResult(r) ->
            Error (sprintf "Unexpected MSBuild result %s" r)
        | Dotnet.ProjInfo.Inspect.GetProjectInfoErrors.MSBuildFailed(exitCode, (workDir, exePath, args)) ->
            let logMsg = [ yield "Log: "; yield! log ] |> String.concat (Environment.NewLine)
            let msbuildErrorMsg =
                [ sprintf "MSBuild failed with exitCode %i" exitCode
                  sprintf "Working Directory: '%s'" workDir
                  sprintf "Exe Path: '%s'" exePath
                  sprintf "Args: '%s'" args ]
                |> String.concat " "

            Error (sprintf "%s%s%s" msbuildErrorMsg (Environment.NewLine) logMsg)
    | _ ->
        Error "error getting msbuild info: internal error"


  let private execProjInfoFromMsbuild msbuildPath (notifyState: WorkspaceProjectState -> unit) (useBinaryLogger: bool) additionalMSBuildProps (solutionFile: string) =

    let projDir = Path.GetDirectoryName solutionFile

    let loggedMessages = ConcurrentQueue<string>()

    let eventHandler = Event<string * list<Result<Inspect.GetResult,Inspect.GetProjectInfoErrors<_>>>> ()
    let handel (name,  lst: list<Result<Inspect.GetResult,Inspect.GetProjectInfoErrors<_>>>) =
        let res =
          mapParseResults lst
          |> Result.map (fun po -> mapProjResults name po)
        match res with
        | Ok (target, po) ->
          WorkspaceProjectState.Loaded(po, Map.empty, false)
          |> notifyState
        | Error e ->
          WorkspaceProjectState.Failed(name, GetProjectOptionsErrors.GenericError(name, e))
          |> notifyState
        ()

    use handler = eventHandler.Publish.Subscribe (handel)

    notifyState (WorkspaceProjectState.Loading (solutionFile, additionalMSBuildProps))

    let getP2PRefs = Inspect.getResolvedP2PRefs
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
          "BaseIntermediateOutputPath"
          "MSBuildProjectFullPath"
        ]
    let gp () = Inspect.getProperties (["TargetPath"; "IsCrossTargetingBuild"; "TargetFrameworks"] @ additionalInfo)

    let getItems () = Inspect.getItems [("Compile", Inspect.GetItemsModifier.FullPath); ("Compile", Inspect.GetItemsModifier.Custom("Link"))] []

    let additionalArgs = additionalMSBuildProps |> List.map (Inspect.MSBuild.MSbuildCli.Property)

    let globalArgs =
        if useBinaryLogger then
            [ Inspect.MSBuild.MSbuildCli.Switch("bl") ]
        else
            match Environment.GetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL") with
            | "1" -> [ Inspect.MSBuild.MSbuildCli.Switch("bl") ]
            | _ -> []

    let runCmd exePath args = Utils.runProcess loggedMessages.Enqueue projDir exePath (args |> String.concat " ")
    let msbuildExec = Inspect.msbuild msbuildPath runCmd
    let infoResult = Inspect.runMsBuild loggedMessages.Enqueue msbuildExec [Inspect.getFscArgs; getP2PRefs; gp; getItems] (additionalArgs @ globalArgs) eventHandler solutionFile



    infoResult, (loggedMessages.ToArray() |> Array.toList)

  /// Runs msbuild on given solution file. Returns `Ok ()` in case of succesful MsBuild run, or `Error string` if MsBuild call failed.
  /// Result doesn't say anything about actuall project parsing, just if it was possible to run MsBuild.
  /// All project parsing result will be published using `notifyState` callback.
  let load msbuildPath (useBinaryLogger: bool) (notifyState: WorkspaceProjectState -> unit) (solutionFile: string) =
    execProjInfoFromMsbuild msbuildPath notifyState useBinaryLogger [] solutionFile
    |> mapMSBuildResults
