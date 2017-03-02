module Inspect

open System.IO
open Medallion.Shell

let runCmd log exePath args =
    log (sprintf "running '%s %s'" exePath (args |> String.concat " "))
    Command.Run(exePath, args |> Array.ofList |> Array.map box)

let dotnetMsbuild run args =
    let dotnetExe = @"dotnet"
    let msbuildArgs = "msbuild" :: args @ ["/nologo"; "/verbosity:quiet"]
    run dotnetExe msbuildArgs

let install_target_file log projPath =
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

  <Target Name="_Inspect_GetProjectReferences">
    <Message Text="%(ProjectReference.FullPath)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_GetProjectReferences_OutFile)' != '' "
            File="$(_Inspect_GetProjectReferences_OutFile)"
            Lines="@(ProjectReference -> '%(FullPath)')"
            Overwrite="true"
            Encoding="UTF-8"/>
  </Target>

  <Target Name="_Inspect_FscArgs"
          DependsOnTargets="ResolveReferences;CoreCompile">
    <Message Text="%(FscCommandLineArgs.Identity)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_Inspect_FscArgs_OutFile)' != '' "
            File="$(_Inspect_FscArgs_OutFile)"
            Lines="@(FscCommandLineArgs -> '%(Identity)')"
            Overwrite="true" 
            Encoding="UTF-8"/>
  </Target>

</Project>
        """.Trim()

    log (sprintf "writing helper target file in '%s'" targetFileDestPath)
    File.WriteAllText(targetFileDestPath, targetFileTemplate)

    Ok targetFileDestPath

type GetResult =
     | FscArgs of string list
     | P2PRefs of string list

let parseFscArgsOut outFile =
    let lines =
        File.ReadAllLines(outFile)
        |> List.ofArray
    Ok (FscArgs lines)

let getFscArgs outFile =
    let args =
        [ "/p:SkipCompilerExecution=true"
          "/p:ProvideCommandLineArgs=true"
          "/p:CopyBuildOutputToOutputDirectory=false"
          "/t:_Inspect_FscArgs"
          sprintf "/p:_Inspect_FscArgs_OutFile=%s" outFile ]
    args, (fun () -> parseFscArgsOut outFile)

let parseP2PRefsOut outFile =
    let lines =
        File.ReadAllLines(outFile)
        |> List.ofArray
    Ok (P2PRefs lines)

let getP2PRefsArgs outFile =
    let args =
        [ "/t:_Inspect_GetProjectReferences"
          sprintf "/p:_Inspect_GetProjectReferences_OutFile=%s" outFile ]
    args, (fun () -> parseP2PRefsOut outFile)
