namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace

type FCS_ProjectOptions = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpChecker

type FCSBinder (netFwInfo: NetFWInfo, workspace: Loader, checker: FCS_Checker) =

    // let projectLoadedSuccessfully projectFileName response =
    //     let project =
    //         match state.Projects.TryFind projectFileName with
    //         | Some prj -> prj
    //         | None ->
    //             let proj = new Project(projectFileName, onChange)
    //             state.Projects.[projectFileName] <- proj
    //             proj
    //     ()

    member this.GetProjectOptions(path: string) =
        ()

type FsxBinder (netFwInfo: NetFWInfo, checker: FCS_Checker) =

    member this.GetProjectOptionsFromScriptBy(tfm, file, source) = async {
      let dummy : FSharpCompilerServiceChecker.CheckerGetProjectOptionsFromScript<FCS_ProjectOptions, _> =
        fun (file, source, otherFlags, assumeDotNetFramework) ->
          checker.GetProjectOptionsFromScript(file, source, otherFlags = otherFlags, assumeDotNetFramework = assumeDotNetFramework)

      let! (rawOptions, mapper) =
        netFwInfo.GetProjectOptionsFromScript(dummy, tfm, file, source)

      let rawOptions = { rawOptions with OtherOptions = mapper rawOptions.OtherOptions }

      return rawOptions
    }
