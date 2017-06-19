module Dotnet.ProjInfo.Inspect

open System.IO

#if NET45
let inline Ok x = Choice1Of2 x
let inline Error x = Choice2Of2 x

let inline (|Ok|Error|) x =
    match x with
    | Choice1Of2 x -> Ok x
    | Choice2Of2 e -> Error e

type private Result<'Ok,'Err> = Choice<'Ok,'Err>

module private Result =
  let map f inp = match inp with Error e -> Error e | Ok x -> Ok (f x)
  let mapError f inp = match inp with Error e -> Error (f e) | Ok x -> Ok x
  let bind f inp = match inp with Error e -> Error e | Ok x -> f x        
#endif

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

let dotnetMsbuild run project args =
    let dotnetExe = @"dotnet"
    let msbuildArgs =
        Project(project) :: args @ [ Switch "nologo"; Switch "verbosity:quiet"]
        |> List.map (MSBuild.sprintfMsbuildArg)
    match run dotnetExe ("msbuild" :: msbuildArgs) with
    | 0, x -> Ok x
    | n, x -> Error (MSBuildFailed (n,x))

let install_target_file log templates projPath =
    let projDir, projName = Path.GetDirectoryName(projPath), Path.GetFileName(projPath)
    let objDir = Path.Combine(projDir, "obj")
    let targetFileDestPath = Path.Combine(objDir, (sprintf "%s.proj-info.targets" projName))

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

    log (sprintf "writing helper target file in '%s'" targetFileDestPath)
    File.WriteAllText(targetFileDestPath, targetFileTemplate.Trim())

    Ok targetFileDestPath

type GetResult =
     | FscArgs of string list
     | P2PRefs of string list
     | ResolvedP2PRefs of ResolvedP2PRefsInfo list
     | Properties of (string * string) list
and ResolvedP2PRefsInfo = { ProjectReferenceFullPath: string; TargetFramework: string; Others: (string * string) list }

let getNewTempFilePath () =
    let outFile = System.IO.Path.GetTempFileName()
    if File.Exists outFile then File.Delete outFile
    outFile

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
    let outFile = getNewTempFilePath ()
    let args =
        [ Property ("SkipCompilerExecution", "true")
          Property ("ProvideCommandLineArgs" , "true")
          Property ("CopyBuildOutputToOutputDirectory", "false")
          Property ("UseCommonOutputDirectory", "true")
          Target "_Inspect_FscArgs"
          Property ("_Inspect_FscArgs_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseFscArgsOut outFile)

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
    let outFile = getNewTempFilePath ()
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
        |> (fun x -> Ok (Properties x))
    | _, err ->
        err
        |> List.choose (function Ok _ -> None | Error x -> Some x)
        |> sprintf "invalid temp file content '%A'"
        |> (fun x -> Error (UnexpectedMSBuildResult x))

let getProperties props =
    let template =
        """
  <Target Name="_Inspect_GetProperties"
          DependsOnTargets="ResolveReferences">
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
    let outFile = getNewTempFilePath ()
    let args =
        [ Target "_Inspect_GetProperties"
          Property ("_Inspect_GetProperties_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parsePropertiesOut outFile)

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
                    match pathOpt, tfmOpt with
                    | Some path, Some tfm ->
                        { ProjectReferenceFullPath = path; TargetFramework = tfm; Others = lines |> List.ofArray }
                    | _ ->
                        failwithf "parsing resolved p2p refs, expected properties not found '%A'" allLines

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
    let outFile = getNewTempFilePath ()
    let args =
        [ Property ("DesignTimeBuild", "true")
          Target "_Inspect_GetResolvedProjectReferences"
          Property ("_Inspect_GetResolvedProjectReferences_OutFile", outFile) ]
    template, args, (fun () -> bindSkipped parseResolvedP2PRefOut outFile)


let getProjectInfos log msbuildExec getters additionalArgs projPath =

    let templates, argsList, parsers = 
        getters
        |> List.map (fun getArgs -> getArgs ())
        |> List.unzip3

    let args = argsList |> List.concat

    projPath
    |> install_target_file log templates
    |> Result.bind (fun _ -> msbuildExec projPath (args @ additionalArgs))
    |> Result.map (fun _ -> parsers |> List.map (fun parse -> parse ()))

let getProjectInfo log msbuildExec getArgs additionalArgs projPath =

    let template, args, parse =  getArgs ()

    projPath
    |> install_target_file log [template]
    |> Result.bind (fun _ -> msbuildExec projPath (args @ additionalArgs))
    |> Result.bind (fun _ -> parse ())
