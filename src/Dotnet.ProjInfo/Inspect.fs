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

type GetResult =
     | FscArgs of string list
     | CscArgs of string list
     | P2PRefs of string list
     | ResolvedP2PRefs of ResolvedP2PRefsInfo list
     | Properties of (string * string) list
     | Items of Map<string, string list>
     | ResolvedNETRefs of string list
     | InstalledNETFw of string list
and ResolvedP2PRefsInfo = { ProjectReferenceFullPath: string; TargetFramework: string option; Others: (string * string) list }

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
    let outFile = getNewTempFilePath "FscArgs.txt"
    let args =
        [ Property ("SkipCompilerExecution", "true")
          Property ("ProvideCommandLineArgs" , "true")
          Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Target "_Inspect_FscArgs"
          Property ("_Inspect_FscArgs_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseFscArgsOut outFile)

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
    let outFile = getNewTempFilePath "CscArgs.txt"
    let args =
        [ Property ("SkipCompilerExecution", "true")
          Property ("ProvideCommandLineArgs" , "true")
          Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Target "_Inspect_CscArgs"
          Property ("_Inspect_CscArgs_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseCscArgsOut outFile)

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
    let outFile = getNewTempFilePath "GetProjectReferences.txt"
    let args =
        [ Target "_Inspect_GetProjectReferences"
          Property ("_Inspect_GetProjectReferences_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseP2PRefsOut outFile)

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
    
    let outFile = getNewTempFilePath "GetProperties.txt"
    let args =
        [ Target "_Inspect_GetProperties"
          Property ("_Inspect_GetProperties_OutFile", outFile) ]
    template, args, (fun () -> outFile
                               |> bindSkipped parsePropertiesOut
                               |> Result.map Properties)


let parseItemsArgsOut outFile =
    let groupByKeyValue =
        List.groupBy fst
        >> Map.ofList
        >> Map.map (fun _ items -> items |> List.map snd)

    outFile
    |> parsePropertiesOut
    |> Result.map groupByKeyValue
    |> Result.map Items

[<RequireQualifiedAccess>]
type GetItemsModifier =
    | Identity
    | FullPath
    | Custom of string

let private getItemsModifierMSBuildProperty modifier =
    match modifier with
    | GetItemsModifier.Identity -> "Identity"
    | GetItemsModifier.FullPath -> "FullPath"
    | GetItemsModifier.Custom c -> c

let getItems items dependsOnTargets =
    let dependsOnTargetsProperty = dependsOnTargets |> String.concat ";"

    let templateSections =
        [ //header
          yield String.Format("""
  <Target Name="_Inspect_Items"
          Condition=" '$(IsCrossTargetingBuild)' != 'true' "
          DependsOnTargets="{0}">
                              """, dependsOnTargetsProperty)

          for (tag, itemName, modifier) in items do
              let itemModifier = getItemsModifierMSBuildProperty modifier
              yield String.Format("""
              <Message Text="{2}=%({0}.{1})" Importance="High" />
              <WriteLinesToFile
                      Condition=" '$(_Inspect_Items_OutFile)' != '' "
                      File="$(_Inspect_Items_OutFile)"
                      Lines="@({0} -> '{2}=%({1})')"
                      Overwrite="false"
                      Encoding="UTF-8"/>
                                  """, itemName, itemModifier, tag).Trim()

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

    let outFile = getNewTempFilePath "Items.txt"
    let args =
        [ Target "_Inspect_Items"
          Property ("_Inspect_Items_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseItemsArgsOut outFile)

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
    let outFile = getNewTempFilePath "GetResolvedProjectReferences.txt"
    let args =
        [ Property ("DesignTimeBuild", "true")
          Target "_Inspect_GetResolvedProjectReferences"
          Property ("_Inspect_GetResolvedProjectReferences_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseResolvedP2PRefOut outFile)

let uninstall_old_target_file log projPath =
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
    |> Result.map (fun _ -> parsers |> List.map (fun parse -> parse ()))

let getProjectInfo log msbuildExec getArgs additionalArgs (projPath: string) =
    //TODO refactor to use getProjectInfosOldSdk
    let template, args, parse =  getArgs ()

    // remove deprecated target file, if exists
    projPath
    |> uninstall_old_target_file log

    getNewTempFilePath "proj-info.hook.targets"
    |> writeTargetFile log [template]
    |> Result.bind (fun targetPath -> msbuildExec projPath (args @ additionalArgs @ [ Property("CustomAfterMicrosoftCommonTargets", targetPath) ]))
    |> Result.bind (fun _ -> parse ())

#if !NETSTANDARD1_6

let getFscArgsOldSdk propsToFscArgs () =

    let props = FakeMsbuildTasks.getFscTaskProperties ()

    let template =
        """
  <!-- Override CoreCompile target -->
  <Target Name="CoreCompile" DependsOnTargets="$(CoreCompileDependsOn)">
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
    let outFile = getNewTempFilePath "CoreCompilePropsOldSdk.txt"
    let args =
        [ Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Property ("BuildingInsideVisualStudio", "true")
          Property ("ShouldUnsetParentConfigurationAndPlatform", "true")
          Target "Build"
          Property ("_Inspect_CoreCompilePropsOldSdk_OutFile", outFile) ]
    template, args, (fun () -> outFile
                               |> bindSkipped parsePropertiesOut
                               |> Result.bind propsToFscArgs
                               |> Result.map FscArgs)

#endif

module ProjectRecognizer =

    type ProjectRecognizedInfo =
        { Language: ProjectLanguage }
    and ProjectLanguage =
        | CSharp
        | FSharp
        | Unknown of string

    let languageOfProject file =
        match Path.GetExtension(file) with
        | ".csproj" -> ProjectLanguage.CSharp
        | ".fsproj" -> ProjectLanguage.FSharp
        | ext -> ProjectLanguage.Unknown ext

    let (|DotnetSdk|OldSdk|Unsupported|) file =
        //Easy way to detect new fsproj is to check the msbuild version of .fsproj
        //Post 1.0 has Sdk attribute (like `Sdk="FSharp.NET.Sdk;Microsoft.NET.Sdk"`), use that
        //for checking .NET Core fsproj.
        let rec getProjectType (sr:StreamReader) limit =
            // post preview5 dropped this, check Sdk field
            let isNetCore (line:string) = line.ToLower().Contains("sdk=")
            if limit = 0 then
                Unsupported // unsupported project type
            else
                let line = sr.ReadLine()
                if not <| line.Contains("ToolsVersion") && not <| line.Contains("Sdk=") then
                    getProjectType sr (limit-1)
                else
                    let projInfo = { Language = languageOfProject file }
                    if isNetCore line then DotnetSdk projInfo else OldSdk projInfo
        use sr = File.OpenText(file)
        getProjectType sr 3
