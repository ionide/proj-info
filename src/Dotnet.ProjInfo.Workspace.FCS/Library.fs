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

        let byKey key (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
          if kv.Key = key then
            Some kv
          else
            None

        let rec getPoByKey (key:ProjectKey) : Result<FCS_ProjectOptions, GetProjectOptionsErrors> =
            match parsed |> Array.tryPick (byKey key) with
            | None -> Error (ProjectNotLoaded key.ProjectPath)
            | Some kv -> getPo kv
        and getPo (kv: KeyValuePair<ProjectKey, ProjectOptions>) : Result<FCS_ProjectOptions, GetProjectOptionsErrors> =
          match kv.Value with
          | po when not (po.ProjectFileName.EndsWith(".fsproj")) ->
              Error (LanguageNotSupported po.ProjectFileName)
          | po ->

              let isGeneratedTfmAssemblyInfoFile path =
                let f = System.IO.Path.GetFileName(path)
                f.StartsWith(".NETFramework,Version=v") && f.EndsWith(".AssemblyAttributes.fs")

              let refs =
                po.ReferencedProjects
                |> List.map (fun key ->
                  getPoByKey { ProjectKey.ProjectPath = key.ProjectFileName; TargetFramework = key.TargetFramework }
                  |> Result.bind (fun po ->
                    match po.ExtraProjectInfo with
                    | Some (:? ProjectOptions as dpwPo) -> Ok (dpwPo.ExtraProjectInfo.TargetPath, po)
                    | Some s -> Error (InvalidExtraProjectInfos (po.ProjectFileName, sprintf "cannot cast to ProjectOptions, was %s" (if isNull s then "<NULL>" else s.GetType().FullName)))
                    | None -> Error (MissingExtraProjectInfos po.ProjectFileName))
                  )

              // maybe don't fail on every error, currently we only ignore LanguageNotSupported
              let errors = refs |> List.choose (function
                | Error (LanguageNotSupported _) -> None // ignore references into other languages
                | Error err -> Some (err.ProjFile, err)
                | _ -> None)
              if errors.Length > 0 then
                Error (ReferencesNotLoaded (po.ProjectFileName, errors))
              else
                let realRefs = refs |> List.choose (function Ok p -> Some p | _ -> None) |> List.toArray

                let fcsPo : FCS_ProjectOptions = {
                    FCS_ProjectOptions.ProjectId = None
                    ProjectFileName = po.ProjectFileName
                    SourceFiles = po.SourceFiles |> List.filter (fun p -> not (isGeneratedTfmAssemblyInfoFile p)) |> Array.ofList
                    OtherOptions = po.OtherOptions |> Array.ofList
                    ReferencedProjects = realRefs
                    IsIncompleteTypeCheckEnvironment = false
                    UseScriptResolutionRules = false
                    LoadTime = po.LoadTime
                    UnresolvedReferences = None
                    OriginalLoadReferences = []
                    Stamp = None
                    ExtraProjectInfo = Some(box po)
                }

                // TODO sanity check: the p2p .dll are in the parent project references, otherwise is strange

                fcsPo
                |> removeDeprecatedArgs
                // TODO check use full paths?
                |> Ok

        let byPath path (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
          if kv.Key.ProjectPath = path then
            Some kv
          else
            None

        match parsed |> Array.tryPick (byPath path) with
        | Some po -> getPo po
        | None ->
          match workspace.LastError path with
          | Some e -> Error e
          | None -> Error (ProjectNotLoaded path)


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
