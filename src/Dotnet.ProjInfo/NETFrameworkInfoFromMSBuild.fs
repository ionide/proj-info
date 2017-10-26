module Dotnet.ProjInfo.NETFrameworkInfoFromMSBuild

open System
open System.IO

#if NET45
open Dotnet.ProjInfo.Inspect
#endif

open Dotnet.ProjInfo.Inspect.MSBuild

let createEnvInfoProj () =
  let createTempDir () =
      let tempPath = Path.GetTempFileName()
      File.Delete(tempPath)
      Directory.CreateDirectory(tempPath).FullName

  let tempDir = createTempDir ()
  let proj = Path.Combine(tempDir, "EnvironmentInfo.proj")
  let projContent = Resources.getResourceFileAsString "EnvironmentInfo.proj"
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


let installedNETFrameworks () =
    let prop = "FrameworkPathOverride"
    let template, args, parser = Inspect.getProperties [prop]

    let find frameworkPathOverride =

      let isTFVersion (name: string) =
        name
        |> fun s -> s.TrimStart('v') // on windows is v4.5
        |> fun s -> s.Replace("-api", "") // on mono is 4.6.1-api
        |> fun s -> s.ToCharArray()
        |> fun s ->
            if s |> Array.except [ yield! ['0' .. '9']; yield '.' ] |> Array.isEmpty
            then Some (String(s))
            else None
        |> Option.map (sprintf "v%s")

      let dir = Path.GetDirectoryName(frameworkPathOverride)
      Directory.GetDirectories(dir)
      |> List.ofArray
      |> List.map Path.GetFileName
      |> List.choose isTFVersion //need to exclude 4.X and others invalid dirs
      |> List.distinct
      |> Inspect.GetResult.InstalledNETFw

    let findInstalledNETFw () =
        parser ()
        |> Result.bind (fun p ->
            match p with
            | Inspect.GetResult.Properties props ->
                match props |> Map.ofList |> Map.tryFind prop with
                | None -> Error (Inspect.GetProjectInfoErrors.UnexpectedMSBuildResult (sprintf "expected Property '%s' not found, found: %A" prop props))
                | Some fpo -> Ok (find fpo)
            | r -> Error (Inspect.GetProjectInfoErrors.UnexpectedMSBuildResult (sprintf "expected Properties result, was %A" r)))

    template, args, findInstalledNETFw
