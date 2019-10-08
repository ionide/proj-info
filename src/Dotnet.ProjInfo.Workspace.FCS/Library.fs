namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace
open System.Collections.Generic

type FCS_ProjectOptions = FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = FSharp.Compiler.SourceCodeServices.FSharpChecker

module internal FCSPoHelpers =

  let rec fcsPoMapper (fcsPoData: FCSProjectOptionsData) : FCS_ProjectOptions =
      { FCS_ProjectOptions.ProjectId = fcsPoData.ProjectId
        ProjectFileName = fcsPoData.ProjectFileName
        SourceFiles = fcsPoData.SourceFiles
        OtherOptions = fcsPoData.OtherOptions
        ReferencedProjects =
          fcsPoData.ReferencedProjects
          |> Array.map (fun (p, d) -> p, (fcsPoMapper d))
        IsIncompleteTypeCheckEnvironment = fcsPoData.IsIncompleteTypeCheckEnvironment
        UseScriptResolutionRules = fcsPoData.UseScriptResolutionRules
        LoadTime = fcsPoData.LoadTime
        UnresolvedReferences = None // it's always None
        OriginalLoadReferences = [] // it's always empty list
        Stamp = fcsPoData.Stamp
        ExtraProjectInfo = fcsPoData.ExtraProjectInfo }

type FCSBinder (netFwInfo: NetFWInfo, workspace: Loader, checker: FCS_Checker) =
    inherit FCSAdapter<FCS_ProjectOptions>(workspace, FCSPoHelpers.fcsPoMapper)

type FsxBinder (netFwInfo: NetFWInfo, checker: FCS_Checker) =

    member this.GetProjectOptionsFromScriptBy(tfm, file, source) = async {
      let dummy : FSharpCompilerServiceChecker.CheckerGetProjectOptionsFromScript<FCS_ProjectOptions, _> =
        fun (file, source, otherFlags, assumeDotNetFramework) ->
          let sourceText = FSharp.Compiler.Text.SourceText.ofString source
          checker.GetProjectOptionsFromScript(file, sourceText, otherFlags = otherFlags, assumeDotNetFramework = assumeDotNetFramework)

      let! (rawOptions, mapper) =
        netFwInfo.GetProjectOptionsFromScript(dummy, tfm, file, source)

      let rawOptions = { rawOptions with OtherOptions = mapper rawOptions.OtherOptions }

      return rawOptions
    }
