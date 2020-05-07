module Dotnet.ProjInfo.Inspect

open System
open System.IO

module MSBuild =
    type MSbuildCli =
         | Property of string * string
         | Target of string
         | Switch of string
         | Project of string

    let sprintfMsbuildArg a =
        let quote (s: string) =
            if s.Contains(" ")
            then sprintf "\"%s\"" s
            else s

        match a with
         | Property (k,"") -> sprintf "\"/p:%s=\"" k
         | Property (k,v) -> sprintf "/p:%s=%s" k v |> quote
         | Target t -> sprintf "/t:%s" t |> quote
         | Switch w -> sprintf "/%s" w
         | Project w -> w |> quote

    let (|ConditionEquals|_|) (str: string) (arg: string) =
        if System.String.Compare(str, arg, System.StringComparison.OrdinalIgnoreCase) = 0
        then Some() else None

    let (|StringList|_|) (str: string)  =
        str.Split([| ';' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray
        |> Some

open MSBuild

type GetProjectInfoErrors<'T> =
    | UnexpectedMSBuildResult of string
    | MSBuildFailed of int * 'T
    | MSBuildSkippedTarget

[<RequireQualifiedAccess>]
type MSBuildExePath =
    | Path of string
    | DotnetMsbuild of dotnetExePath: string

let disableEnvVar envVarName =
    let oldEnv =
        match Environment.GetEnvironmentVariable(envVarName) with
        | null -> None
        | s ->
            Environment.SetEnvironmentVariable(envVarName, null)
            Some s
    { new IDisposable with
        member x.Dispose() =
            match oldEnv with
            | None -> ()
            | Some s -> Environment.SetEnvironmentVariable(envVarName, s) }

let msbuild msbuildExe run project args =
    let exe, beforeArgs =
        match msbuildExe with
        | MSBuildExePath.Path path -> path, []
        | MSBuildExePath.DotnetMsbuild path -> path, ["msbuild"]
    let msbuildArgs =
        Project(project) :: args @ [ Switch "nologo"; Switch "verbosity:quiet"]
        |> List.map (MSBuild.sprintfMsbuildArg)

    //HACK disable FrameworkPathOverride on msbuild, to make installedNETFrameworks work.
    //     That env var is used only in .net sdk to workaround missing gac assemblies on unix
    use disableFrameworkOverrideOnMsbuild =
        match msbuildExe with
        | MSBuildExePath.Path _ -> disableEnvVar "FrameworkPathOverride"
        | MSBuildExePath.DotnetMsbuild _ -> { new IDisposable with member x.Dispose() = () }

    match run exe (beforeArgs @ msbuildArgs) with
    | 0, x -> Ok x
    | n, x -> Error (MSBuildFailed (n,x))

let dotnetMsbuild run project args =
    msbuild (MSBuildExePath.DotnetMsbuild "dotnet") run project args

let writeTargetFile log templates targetFileDestPath =
    // https://github.com/dotnet/cli/issues/5650

    let targetFileTemplate =
        """
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<Project ToolsVersion="14.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <MSBuildAllProjects>$(MSBuildAllProjects);$(MSBuildThisFileFullPath)</MSBuildAllProjects>
  </PropertyGroup>
        """
        + (templates |> String.concat (System.Environment.NewLine))
        +
        """
</Project>
        """

    let targetFileOnDisk =
        if File.Exists(targetFileDestPath) then
            try
                Some (File.ReadAllText targetFileDestPath)
            with
            | _ -> None
        else
            None

    let newTargetFile = targetFileTemplate.Trim()

    if targetFileOnDisk <> Some newTargetFile then
        log (sprintf "writing helper target file in '%s'" targetFileDestPath)
        File.WriteAllText(targetFileDestPath, newTargetFile)

    Ok targetFileDestPath

[<RequireQualifiedAccess>]
type GetItemsModifier =
    | Identity
    | FullPath
    | Custom of string

type GetItemResult =
    { Name: string
      Identity: string
      Metadata: (GetItemsModifier * string) list }

type GetResult =
     | FscArgs of string list
     | CscArgs of string list
     | P2PRefs of string list
     | ResolvedP2PRefs of ResolvedP2PRefsInfo list
     | Properties of (string * string) list
     | Items of GetItemResult list
     | ResolvedNETRefs of string list
     | InstalledNETFw of string list
and ResolvedP2PRefsInfo = { ProjectReferenceFullPath: string; TargetFramework: string option; Others: (string * string) list }

let outDir = Path.Combine(Path.GetTempPath(), "dotnet-proj-info")

let cleanOutDir () =
    if Directory.Exists outDir then
        Directory.Delete(outDir, true)

    Directory.CreateDirectory outDir
    |> ignore

let getNewTempFilePath suffix =
    let outFile = System.IO.Path.GetTempFileName()
    if File.Exists outFile then File.Delete outFile
    sprintf "%s.%s" outFile suffix

let bindSkipped f outFile =
    if not(File.Exists outFile) then
        Error MSBuildSkippedTarget
    else
        f outFile

let parseFscArgsOut outFile =
    let lines =
        File.ReadAllLines(outFile)
        |> List.ofArray
    Ok (FscArgs lines)

let getFscArgs () =
    let template =
        """
  <Target Name="_Inspect_FscArgs"
          Condition=" '$(IsCrossTargetingBuild)' != 'true' "
          DependsOnTargets="ResolveReferences;CoreCompile">
    <PropertyGroup>
        <_Inspect_FscArgs_OutFile>$(_Inspect_FscArgs_OutDir)\\$(MSBuildProjectFile)_FscArgs.txt</_Inspect_FscArgs_OutFile>
    </PropertyGroup>
    <Message Text="%(FscCommandLineArgs.Identity)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_FscArgs_OutFile)' != '' "
            File="$(_Inspect_FscArgs_OutFile)"
            Lines="@(FscCommandLineArgs -> '%(Identity)')"
            Overwrite="true"
            Encoding="UTF-8"/>
    <!-- WriteLinesToFile doesnt create the file if @(FscCommandLineArgs) is empty -->
    <Touch
        Condition=" '$(_Inspect_FscArgs_OutFile)' != '' "
        Files="$(_Inspect_FscArgs_OutFile)"
        AlwaysCreate="True" />
  </Target>
        """.Trim()
    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_FscArgs.txt" outDir pp
    let args =
        [ Property ("SkipCompilerExecution", "true")
          Property ("ProvideCommandLineArgs" , "true")
          Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Target "_Inspect_FscArgs"
          Property ("_Inspect_FscArgs_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> bindSkipped parseFscArgsOut (outFile projectPath))

let parseCscArgsOut outFile =
    let lines =
        File.ReadAllLines(outFile)
        |> List.ofArray
    Ok (CscArgs lines)

let getCscArgs () =
    let template =
        """
  <Target Name="_Inspect_CscArgs"
          Condition=" '$(IsCrossTargetingBuild)' != 'true' "
          DependsOnTargets="ResolveReferences;CoreCompile">
    <Message Text="%(CscCommandLineArgs.Identity)" Importance="High" />
    <PropertyGroup>
        <_Inspect_CscArgs_OutFile>$(_Inspect_CscArgs_OutDir)\\$(MSBuildProjectFile)_CscArgs.txt</_Inspect_CscArgs_OutFile>
    </PropertyGroup>
    <WriteLinesToFile
            Condition=" '$(_Inspect_CscArgs_OutFile)' != '' "
            File="$(_Inspect_CscArgs_OutFile)"
            Lines="@(CscCommandLineArgs -> '%(Identity)')"
            Overwrite="true"
            Encoding="UTF-8"/>
    <!-- WriteLinesToFile doesnt create the file if @(CscCommandLineArgs) is empty -->
    <Touch
        Condition=" '$(_Inspect_CscArgs_OutFile)' != '' "
        Files="$(_Inspect_CscArgs_OutFile)"
        AlwaysCreate="True" />
  </Target>
        """.Trim()
    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_CscArgs.txt" outDir pp
    let args =
        [ Property ("SkipCompilerExecution", "true")
          Property ("ProvideCommandLineArgs" , "true")
          Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Target "_Inspect_CscArgs"
          Property ("_Inspect_CscArgs_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> bindSkipped parseCscArgsOut (outFile projectPath))

let parseP2PRefsOut outFile =
    let lines =
        File.ReadAllLines(outFile)
        |> List.ofArray
    Ok (P2PRefs lines)

let getP2PRefs () =
    let template =
        """
  <Target Name="_Inspect_GetProjectReferences">
    <Message Text="%(ProjectReference.FullPath)" Importance="High" />
    <PropertyGroup>
        <_Inspect_GetProjectReferences_OutFile>$(_Inspect_GetProjectReferences_OutDir)\\$(MSBuildProjectFile)_GetProjectReferences.txt</_Inspect_GetProjectReferences_OutFile>
    </PropertyGroup>
    <WriteLinesToFile
            Condition=" '$(_Inspect_GetProjectReferences_OutFile)' != '' "
            File="$(_Inspect_GetProjectReferences_OutFile)"
            Lines="@(ProjectReference -> '%(FullPath)')"
            Overwrite="true"
            Encoding="UTF-8"/>
    <!-- WriteLinesToFile doesnt create the file if @(ProjectReference) is empty -->
    <Touch
        Condition=" '$(_Inspect_GetProjectReferences_OutFile)' != '' "
        Files="$(_Inspect_GetProjectReferences_OutFile)"
        AlwaysCreate="True" />
  </Target>
        """.Trim()
    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_GetProjectReferences.txt" outDir pp
    let args =
        [ Target "_Inspect_GetProjectReferences"
          Property ("_Inspect_GetProjectReferences_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> bindSkipped parseP2PRefsOut (outFile projectPath))

let parsePropertiesOut outFile =
    let firstAndRest (delim: char) (s: string) =
        match s.IndexOf(delim) with
        | -1 -> None
        | index -> Some(s.Substring(0, index), s.Substring(index + 1))

    let lines =
        File.ReadAllLines(outFile)
        |> Array.filter (fun s -> s.Length > 0)
        |> Array.map (fun s -> match s |> firstAndRest '=' with Some x -> Ok x | None -> Error s)
        |> List.ofArray

    match lines |> List.partition (function Ok _ -> true | Error _ -> false) with
    | l, [] ->
        l
        |> List.choose (function Ok x -> Some x | Error _ -> None)
        |> (fun x -> Ok x)
    | _, err ->
        err
        |> List.choose (function Ok _ -> None | Error x -> Some x)
        |> sprintf "invalid temp file content '%A'"
        |> (fun x -> Error (UnexpectedMSBuildResult x))

let getProperties props =
    let templateF isCrossgen =
        """
  <Target Name="_Inspect_GetProperties_""" + (if isCrossgen then "CrossGen" else "NotCrossGen") + """"
          Condition=" '$(IsCrossTargetingBuild)' """ + (if isCrossgen then "==" else "!=") + """ 'true' "
          """ + (if isCrossgen then "" else "DependsOnTargets=\"ResolveReferences\"" ) + """ >
    <PropertyGroup>
        <_Inspect_GetProperties_OutFile>$(_Inspect_GetProperties_OutDir)\\$(MSBuildProjectFile)_GetProperties.txt</_Inspect_GetProperties_OutFile>
    </PropertyGroup>
    <ItemGroup>
        """
        + (
            props
            |> List.mapi (fun i p -> sprintf """
        <_Inspect_GetProperties_OutLines Include="P%i">
            <PropertyName>%s</PropertyName>
            <PropertyValue>$(%s)</PropertyValue>
        </_Inspect_GetProperties_OutLines>
                                             """ i p p)
            |> List.map (fun s -> s.TrimEnd())
            |> String.concat (System.Environment.NewLine) )
        +
        """
    </ItemGroup>
    <Message Text="%(_Inspect_GetProperties_OutLines.PropertyName)=%(_Inspect_GetProperties_OutLines.PropertyValue)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_GetProperties_OutFile)' != '' "
            File="$(_Inspect_GetProperties_OutFile)"
            Lines="@(_Inspect_GetProperties_OutLines -> '%(PropertyName)=%(PropertyValue)')"
            Overwrite="true"
            Encoding="UTF-8"/>
  </Target>
        """.Trim()

    //doing like that (crossgen/notcrossgen) because ResolveReferences doesnt exists
    //if is crossgen

    let templateAll =
        """
  <Target Name="_Inspect_GetProperties"
          DependsOnTargets="_Inspect_GetProperties_CrossGen;_Inspect_GetProperties_NotCrossGen" />
        """

    let template =
        [ templateF true
          templateF false
          templateAll ]
        |> String.concat (System.Environment.NewLine)

    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_GetProperties.txt" outDir pp
    let args =
        [ Target "_Inspect_GetProperties"
          Property ("_Inspect_GetProperties_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> (outFile projectPath) |> bindSkipped parsePropertiesOut |> Result.map Properties)


let getItemsModifierMSBuildProperty modifier =
    match modifier with
    | GetItemsModifier.Identity -> "Identity"
    | GetItemsModifier.FullPath -> "FullPath"
    | GetItemsModifier.Custom c -> c

let parseItemsModifierMSBuildProperty modifier =
    match modifier with
    | "Identity" -> GetItemsModifier.Identity
    | "FullPath" -> GetItemsModifier.FullPath
    | c -> GetItemsModifier.Custom c

let parseItemPath (s: string) =
    //TODO safer, using splitAt function
    let x = s.Split('.') in x.[0], parseItemsModifierMSBuildProperty x.[1]

let parseItemsArgsOut outFile =
    let groupByKeyValue (items: (string * string) list) =
        let getItem flatInfo =
            flatInfo
            |> List.groupBy (fun (name,_,_) -> name)
            |> List.map (fun (itemName, data) ->
                let metadata = data |> List.map (fun (_, k, v) -> k,v)
                let identity, others =
                    metadata
                    |> List.partition (fun (m,_) -> m = GetItemsModifier.Identity)
                    |> fun (ids, others) -> List.head ids, others // check identity exists
                { GetItemResult.Name = itemName
                  Identity = snd identity
                  Metadata = others }
                )

        items
        |> List.map (fun (k, v) -> parseItemPath k, v) // parse Compile.Identity
        |> List.groupBy (fun ((name, _), _) -> name) // by name, like Compile
        // group by item
        |> List.collect (fun (_, items) -> items |> List.groupBy (fun (k, _) -> k) |> List.map (fun (k, v) -> k, v |> List.indexed |> List.map (fun (i, ((n,m), v)) -> i,n,m,v)))
        |> List.collect snd
        |> List.groupBy (fun (i,_,_,_) -> i)
        |> List.map snd
        |> List.map (List.map (fun (_,n,m,v) -> n,m,v))
        // map the items
        |> List.collect getItem

    outFile
    |> parsePropertiesOut
    |> Result.map groupByKeyValue
    |> Result.map Items

let getItems items dependsOnTargets =
    let dependsOnTargetsProperty = dependsOnTargets |> String.concat ";"

    let formatTag itemName modifier = sprintf "%s.%s" itemName (getItemsModifierMSBuildProperty modifier)

    let additionalItemsToGetIdentity =
        let hasIdentity itemsForName =
            itemsForName
            |> List.map snd
            |> List.contains GetItemsModifier.Identity

        items
        |> List.groupBy (fun (_itemName, _) -> _itemName)
        |> List.filter (fun (itemName, itemsForName) -> not (hasIdentity itemsForName))
        |> List.map fst
        |> List.map (fun itemName ->
            let modifier = GetItemsModifier.Identity
            itemName, modifier)

    let allItems =
        List.append additionalItemsToGetIdentity items
        |> List.sortBy (fun (itemName, modifier) -> formatTag itemName modifier)

    let templateSections =
        [ //header
          yield String.Format("""
  <Target Name="_Inspect_Items"
          Condition=" '$(IsCrossTargetingBuild)' != 'true' "
          DependsOnTargets="{0}">
          <PropertyGroup>
            <_Inspect_Items_OutFile>$(_Inspect_Items_OutDir)\\$(MSBuildProjectFile)_Items.txt</_Inspect_Items_OutFile>
          </PropertyGroup>
                              """, dependsOnTargetsProperty)

          for (itemName, modifier) in allItems do
              let itemModifier = getItemsModifierMSBuildProperty modifier
              yield String.Format("""
              <Message Text="{0}.{1}=%({0}.{1})" Importance="High" />
              <WriteLinesToFile
                      Condition=" '$(_Inspect_Items_OutFile)' != '' "
                      File="$(_Inspect_Items_OutFile)"
                      Lines="@({0} -> '{0}.{1}=%({1})')"
                      Overwrite="false"
                      Encoding="UTF-8"/>
                                  """, itemName, itemModifier).Trim()

          yield """
        <!-- WriteLinesToFile doesnt create the file if an item list is empty -->
        <Touch
            Condition=" '$(_Inspect_Items_OutFile)' != '' "
            Files="$(_Inspect_Items_OutFile)"
            AlwaysCreate="True" />
        </Target>
                """
        ]

    let template =
        templateSections
        |> List.map (fun s -> s.Trim())
        |> String.concat (Environment.NewLine)

    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_Items.txt" outDir pp
    let args =
        [ Target "_Inspect_Items"
          Property ("_Inspect_Items_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> bindSkipped parseItemsArgsOut (outFile projectPath))

let parseResolvedP2PRefOut outFile =
    /// Example:
    /// ProjectReferenceFullPath=..\l1.fsproj;TargetFramework=net45;ProjectHasSingleTargetFramework=false;ProjectIsRidAgnostic=true

    let allLines = File.ReadAllLines(outFile)

    let lines =
        allLines
        |> Array.map (fun s -> s.Trim())
        |> Array.filter ((<>) "")
        |> Array.collect (fun s -> s.Split([| ';' |], System.StringSplitOptions.RemoveEmptyEntries))
        |> Array.map (fun s -> s.Split([| '=' |], System.StringSplitOptions.RemoveEmptyEntries))
        |> Array.map (fun s ->
            match s with
            | [| k; v |] -> k,v
            | [| k |] -> k,""
            | _ -> failwithf "parsing resolved p2p refs, invalid key value '%A'" s)

    let p2ps =
        match lines with
        | [| |] -> []
        | lines ->
            let g =
                lines
                |> Array.mapFold (fun s (k,v) ->
                    let i = if k = "ProjectReferenceFullPath" then s + 1 else s
                    (i,k,v), i) 0
                |> fst
                |> Array.groupBy (fun (i,k,v) -> i)
                |> Array.map (fun (_,items) -> items |> Array.map (fun (i,k,v) -> k,v))
            g
            |> Array.map (fun lines ->
                    let props = lines |> Map.ofArray
                    let pathOpt = props |> Map.tryFind "ProjectReferenceFullPath"
                    let tfmOpt = props |> Map.tryFind "TargetFramework"
                    match pathOpt with
                    | Some path ->
                        { ProjectReferenceFullPath = path; TargetFramework = tfmOpt; Others = lines |> List.ofArray }
                    | None ->
                        failwithf "parsing resolved p2p refs, expected property 'ProjectReferenceFullPath' not found. Was '%A'" allLines

                )
            |> List.ofArray

    Ok (ResolvedP2PRefs p2ps)

let getResolvedP2PRefs () =
    let template =
        """
  <Target Name="_Inspect_GetResolvedProjectReferences"
          Condition=" '$(IsCrossTargetingBuild)' != 'true' "
          DependsOnTargets="ResolveProjectReferencesDesignTime">
    <PropertyGroup>
        <_Inspect_GetResolvedProjectReferences_OutFile>$(_Inspect_GetResolvedProjectReferences_OutDir)\\$(MSBuildProjectFile)_GetResolvedProjectReferences.txt</_Inspect_GetResolvedProjectReferences_OutFile>
    </PropertyGroup>
    <Message Text="%(_MSBuildProjectReferenceExistent.FullPath)" Importance="High" />
    <Message Text="%(_MSBuildProjectReferenceExistent.SetTargetFramework)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_GetResolvedProjectReferences_OutFile)' != '' "
            File="$(_Inspect_GetResolvedProjectReferences_OutFile)"
            Lines="@(_MSBuildProjectReferenceExistent -> 'ProjectReferenceFullPath=%(FullPath);%(SetTargetFramework)')"
            Overwrite="true"
            Encoding="UTF-8"/>
    <!-- WriteLinesToFile doesnt create the file if @(_MSBuildProjectReferenceExistent) is empty -->
    <Touch
        Condition=" '$(_Inspect_GetResolvedProjectReferences_OutFile)' != '' "
        Files="$(_Inspect_GetResolvedProjectReferences_OutFile)"
        AlwaysCreate="True" />
  </Target>
        """.Trim()
    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_GetResolvedProjectReferences.txt" outDir pp
    let args =
        [ Property ("DesignTimeBuild", "true")
          Target "_Inspect_GetResolvedProjectReferences"
          Property ("_Inspect_GetResolvedProjectReferences_OutDir", outDir) ]
    template, args, (fun (projectPath : string) -> bindSkipped parseResolvedP2PRefOut (outFile projectPath))

let uninstall_old_target_file log (projPath: string) =
    let projDir, projName = Path.GetDirectoryName(projPath), Path.GetFileName(projPath)
    let objDir = Path.Combine(projDir, "obj")
    let targetFileDestPath = Path.Combine(objDir, (sprintf "%s.proj-info.targets" projName))

    log (sprintf "searching deprecated target file in '%s'." targetFileDestPath)
    if File.Exists targetFileDestPath then
        log (sprintf "found deprecated target file in '%s', deleting." targetFileDestPath)
        File.Delete targetFileDestPath

let getProjectInfos log msbuildExec getters additionalArgs projPath =

    let templates, argsList, parsers =
        getters
        |> List.map (fun getArgs -> getArgs ())
        |> List.unzip3

    let args = argsList |> List.concat

    // remove deprecated target file, if exists
    projPath
    |> uninstall_old_target_file log

    getNewTempFilePath "proj-info.hook.targets"
    |> writeTargetFile log templates
    |> Result.bind (fun targetPath -> msbuildExec projPath (args @ additionalArgs @ [ Property("CustomAfterMicrosoftCommonTargets", targetPath); Property("CustomAfterMicrosoftCommonCrossTargetingTargets", targetPath) ]))
    |> Result.map (fun _ -> parsers |> List.map (fun parse -> parse projPath))

let getProjectInfo log msbuildExec getArgs (projPath: string) =
    //TODO refactor to use getProjectInfosOldSdk
    let template, args, parse = getArgs ()

    // remove deprecated target file, if exists
    projPath
    |> uninstall_old_target_file log

    getNewTempFilePath "proj-info.hook.targets"
    |> writeTargetFile log [template]
    |> Result.bind (fun targetPath -> msbuildExec projPath (args @ [ Property("CustomAfterMicrosoftCommonTargets", targetPath) ]))
    |> Result.bind (fun _ -> parse projPath)

/// Runs MsBuild on given project or solution. Blocking call. Result shows if the msbuild managed to run, not an actuall content of the project files.
///
let runMsBuild log msbuildExec getters additionalArgs (notify: Event<_>) (path: string) =

    let templates, argsList, parsers =
        getters
        |> List.map (fun getArgs -> getArgs ())
        |> List.unzip3

    let args = argsList |> List.concat

    // remove deprecated target file, if exists
    path
    |> uninstall_old_target_file log

    use watcher = new FileSystemWatcher(outDir, "*_Items.txt")
    watcher.Created.Add (fun t ->
        let projName = t.Name.Split('_').[0]
        let res =
            parsers
            |> List.map (fun parse ->
                try
                    parse projName
                with
                | :? System.IO.IOException ->
                    Threading.Thread.Sleep 50
                    parse projName)
        notify.Trigger (projName, res)

        ())

    watcher.EnableRaisingEvents <- true

    getNewTempFilePath "proj-info.hook.targets"
    |> writeTargetFile log templates
    |> Result.bind (fun targetPath -> msbuildExec path (args @ additionalArgs @ [ Property("CustomAfterMicrosoftCommonTargets", targetPath); Property("CustomAfterMicrosoftCommonCrossTargetingTargets", targetPath) ]))

let getFscArgsOldSdk propsToFscArgs () =

    let props = FakeMsbuildTasks.getFscTaskProperties ()

    let template =
        """
  <!-- Override CoreCompile target -->
  <Target Name="CoreCompile" DependsOnTargets="$(CoreCompileDependsOn)">
    <PropertyGroup>
        <_Inspect_CoreCompilePropsOldSdk_OutFile>$(_Inspect_CoreCompilePropsOldSdk_OutDir)\\$(MSBuildProjectFile)_CoreCompilePropsOldSdk.txt</_Inspect_CoreCompilePropsOldSdk_OutFile>
    </PropertyGroup>
    <ItemGroup>
        """
        + (
            props
            |> List.mapi (fun i (p,v) -> sprintf """
        <_Inspect_CoreCompilePropsOldSdk_OutLines Include="P%i">
            <PropertyName>%s</PropertyName>
            <PropertyValue>%s</PropertyValue>
        </_Inspect_CoreCompilePropsOldSdk_OutLines>
                                             """ i p v)
            |> List.map (fun s -> s.TrimEnd())
            |> String.concat (System.Environment.NewLine) )
        +
        """
    </ItemGroup>
    <Message Text="%(_Inspect_CoreCompilePropsOldSdk_OutLines.PropertyName)=%(_Inspect_CoreCompilePropsOldSdk_OutLines.PropertyValue)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_CoreCompilePropsOldSdk_OutFile)' != '' "
            File="$(_Inspect_CoreCompilePropsOldSdk_OutFile)"
            Lines="@(_Inspect_CoreCompilePropsOldSdk_OutLines -> '%(PropertyName)=%(PropertyValue)')"
            Overwrite="true"
            Encoding="UTF-8"/>
  </Target>
        """.Trim()
    let outFile projectPath =
        let pp = Path.GetFileName projectPath
        sprintf "%s\\%s_CoreCompilePropsOldSdk.txt" outDir pp
    let args =
        [ Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Property ("BuildingInsideVisualStudio", "true")
          Property ("ShouldUnsetParentConfigurationAndPlatform", "true")
          Target "Build"
          Property ("_Inspect_CoreCompilePropsOldSdk_OutDir", outDir) ]
    template, args, (fun (projectPath : string) ->
                        (outFile projectPath)
                        |> bindSkipped parsePropertiesOut
                        |> Result.bind propsToFscArgs
                        |> Result.map FscArgs)

module ProjectLanguageRecognizer =

    type ProjectLanguage =
        | CSharp
        | FSharp
        | Unknown of string

    let languageOfProject (file: string) =
        match Path.GetExtension(file) with
        | ".csproj" -> ProjectLanguage.CSharp
        | ".fsproj" -> ProjectLanguage.FSharp
        | ext -> ProjectLanguage.Unknown ext
