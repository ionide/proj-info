module Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild

open System
open System.IO

open Dotnet.ProjInfo.Inspect.MSBuild

type private FrameworkInfoFromMsbuild = {
    ReferencePath: string list
  }

let createEnvInfoProj () =
  let createTempDir () =
      let tempPath = Path.GetTempFileName()
      File.Delete(tempPath)
      Directory.CreateDirectory(tempPath).FullName

  let tempDir = createTempDir ()
  let proj = Path.Combine(tempDir, "EnvironmentInfo.proj")
  let projContent = FakeMsbuildTasks.getResourceFileAsString "EnvironmentInfo.proj"
  File.WriteAllText(proj, projContent)
  proj

let getReferencePaths props =

    let template =
        """
  <ItemGroup>
        """
      + (
          props
          |> List.map (sprintf """<Reference Include="%s" />""")
          |> String.concat (System.Environment.NewLine) )
      +
        """
  </ItemGroup>

  <Target Name="_GetFsxScriptReferences" DependsOnTargets="ResolveAssemblyReferences">
    <Message Text="ReferencePath=%(ReferencePath.Identity)" Importance="High" />
    <WriteLinesToFile
            Condition=" '$(_GetFsxScriptReferences_OutFile)' != '' "
            File="$(_GetFsxScriptReferences_OutFile)"
            Lines="@(ReferencePath -> 'ReferencePath=%(Identity)')"
            Overwrite="true"
            Encoding="UTF-8"/>
            
    <!-- WriteLinesToFile doesnt create the file if @(ReferencePath) is empty -->
    <Touch
        Condition=" '$(_GetFsxScriptReferences_OutFile)' != '' "
        Files="$(_GetFsxScriptReferences_OutFile)"
        AlwaysCreate="True" />
  </Target>
        """.Trim()
    let outFile = Inspect.getNewTempFilePath "fsxScriptReferences.txt"
    let args =
        [ Target "_GetFsxScriptReferences"
          Property ("_GetFsxScriptReferences_OutFile", outFile) ]

    // { TargetFrameworkRootPath = lines |> List.tryPick (chooseByPrefix "TargetFrameworkRootPath=")
    //   FrameworkPathOverride = lines |> List.tryPick (chooseByPrefix "FrameworkPathOverride=")
    //   ReferencePath = lines |> List.choose (chooseByPrefix "ReferencePath=") }

    template, args, (fun () -> outFile
                               |> Inspect.bindSkipped Inspect.parsePropertiesOut
                               |> Result.map (List.map snd >> Inspect.GetResult.ResolvedNETRefs))

