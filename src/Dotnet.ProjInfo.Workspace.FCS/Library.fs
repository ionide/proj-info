namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace
open System.Collections.Generic

type FCS_ProjectOptions = FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = FSharp.Compiler.SourceCodeServices.FSharpChecker

module FscArguments =

    let isTempFile (name: string) =
        let tempPath = System.IO.Path.GetTempPath()
        let s = name.ToLower()
        s.StartsWith(tempPath.ToLower())

    let isDeprecatedArg n =
      // TODO put in FCS
      (n = "--times") || (n = "--no-jit-optimize")

type FCSBinder (netFwInfo: NetFWInfo, workspace: Loader, checker: FCS_Checker) =

    let removeDeprecatedArgs (opts: FCS_ProjectOptions) =
      // TODO add test
      let oos =
        opts.OtherOptions
        |> Array.filter (fun n -> not (FscArguments.isDeprecatedArg n))
      { opts with OtherOptions = oos }

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
          | Some po when not (po.ProjectFileName.EndsWith(".fsproj")) ->
              // FCS doest support others languages
              None
          | Some po ->

              let isGeneratedTfmAssemblyInfoFile path =
                let f = System.IO.Path.GetFileName(path)
                f.StartsWith(".NETFramework,Version=v") && f.EndsWith(".AssemblyAttributes.fs")

              let fcsPo : FCS_ProjectOptions = {
                  FCS_ProjectOptions.ProjectId = None
                  ProjectFileName = po.ProjectFileName
                  SourceFiles = po.SourceFiles |> List.filter (fun p -> not (isGeneratedTfmAssemblyInfoFile p)) |> Array.ofList
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
                  ExtraProjectInfo = Some(box po)
              }

              fcsPo
              |> removeDeprecatedArgs
              // TODO check use full paths?
              |> Some

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
