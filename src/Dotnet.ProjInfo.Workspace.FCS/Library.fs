namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace
open System.Collections.Generic

type FCS_ProjectOptions = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpChecker

type FCSBinder (netFwInfo: NetFWInfo, workspace: Loader, checker: FCS_Checker) =

    member this.GetProjectOptions(path: string) =
        let parsed = workspace.Projects

        let rec getPo key : FCS_ProjectOptions option =

          let byKey key (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
            if kv.Key = key then
              Some kv.Value
            else
              None

          match parsed |> Array.tryPick (byKey key) with
          | None ->
              None
          | Some po ->

              Some ({
                  FCS_ProjectOptions.ProjectId = po.ProjectId
                  ProjectFileName = po.ProjectFileName
                  SourceFiles = [||] // TODO set source files
                  OtherOptions = po.OtherOptions |> Array.ofList
                  ReferencedProjects =
                    po.ReferencedProjects
                     // TODO choose will skip the if not found, should instead log or better
                    |> List.choose (fun key -> getPo { ProjectKey.ProjectPath = key.ProjectFileName; TargetFramework = key.TargetFramework })
                    // Is (path * projectOption) ok? or was .dll?
                    |> List.map (fun po -> (key.ProjectPath, po) )
                    |> Array.ofList
                  IsIncompleteTypeCheckEnvironment = false
                  UseScriptResolutionRules = false
                  LoadTime = po.LoadTime
                  UnresolvedReferences = None
                  OriginalLoadReferences = []
                  Stamp = None
                  ExtraProjectInfo = None
              })

        let byPath path (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
          if kv.Key.ProjectPath = path then
            Some kv.Key
          else
            None

        parsed
        |> Array.tryPick (byPath path)
        |> Option.bind getPo

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
