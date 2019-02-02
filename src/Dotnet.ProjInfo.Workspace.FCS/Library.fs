namespace Dotnet.ProjInfo.Workspace.FCS

open Dotnet.ProjInfo.Workspace
open System.Collections.Generic

type FCS_ProjectOptions = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = Microsoft.FSharp.Compiler.SourceCodeServices.FSharpChecker

module FscArguments =

    let isCompileFile (s:string) =
        s.EndsWith(".fs") || s.EndsWith (".fsi")

    let compileFiles =
        //TODO filter the one without initial -
        List.filter isCompileFile

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

    let compileFiles po =
        let sources = FscArguments.compileFiles po.OtherOptions
        match po.ExtraProjectInfo.ProjectSdkType with
        | ProjectSdkType.Verbose _ ->
            //compatibility with old behaviour (projectcracker), so test output is exactly the same
            //the temp source files (like generated assemblyinfo.fs) are not added to sources
            sources
            |> List.filter (fun p -> not(FscArguments.isTempFile p))

        | ProjectSdkType.DotnetSdk _ ->
            sources

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

              let fcsPo : FCS_ProjectOptions = {
                  FCS_ProjectOptions.ProjectId = po.ProjectId
                  ProjectFileName = po.ProjectFileName
                  SourceFiles = [||] // TODO set source files?
                  OtherOptions =
                    po.OtherOptions
                    |> Array.ofList
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
              }

              fcsPo
              |> removeDeprecatedArgs
              // TODO deduplicateReferences, why?
              // TODO check use full paths
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
