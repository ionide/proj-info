namespace Dotnet.ProjInfo.Workspace

open System
open System.Collections.Generic

type FCSProjectOptionsData =
    {
      // Note that this may not reduce to just the project directory, because there may be two projects in the same directory.
      ProjectFileName: string

      /// This is the unique identifier for the project, it is case sensitive. If it's None, will key off of ProjectFileName in our caching.
      ProjectId: string option

      /// The files in the project
      SourceFiles: string[]

      /// Additional command line argument options for the project. These can include additional files and references.
      OtherOptions: string[]

      /// The command line arguments for the other projects referenced by this project, indexed by the
      /// exact text used in the "-r:" reference in FSharpProjectOptions.
      ReferencedProjects: (string * FCSProjectOptionsData)[]

      /// When true, the typechecking environment is known a priori to be incomplete, for
      /// example when a .fs file is opened outside of a project. In this case, the number of error
      /// messages reported is reduced.
      IsIncompleteTypeCheckEnvironment : bool

      /// When true, use the reference resolution rules for scripts rather than the rules for compiler.
      UseScriptResolutionRules : bool

      /// Timestamp of project/script load, used to differentiate between different instances of a project load.
      /// This ensures that a complete reload of the project or script type checking
      /// context occurs on project or script unload/reload.
      LoadTime : DateTime

      /// Unused in this API and should be 'None' when used as user-specified input
      UnresolvedReferences : obj option

      /// Unused in this API and should be '[]' when used as user-specified input
      OriginalLoadReferences: obj list

      /// Extra information passed back on event trigger
      ExtraProjectInfo : obj option

      /// An optional stamp to uniquely identify this set of options
      /// If two sets of options both have stamps, then they are considered equal
      /// if and only if the stamps are equal
      Stamp: int64 option
    }

type FCSAdapter<'FCSProjectOptions>(workspace: Loader, toFCSPoMapper: FCSProjectOptionsData -> 'FCSProjectOptions) =

    member this.GetProjectOptions(path: string) : Result<'FCSProjectOptions, GetProjectOptionsErrors> =
        let parsed = workspace.Projects

        let byKey key (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
          if kv.Key = key then
            Some kv
          else
            None

        let rec getPoByKey (key:ProjectKey) : Result<FCSProjectOptionsData, GetProjectOptionsErrors> =
            match parsed |> Array.tryPick (byKey key) with
            | None -> Error (ProjectNotLoaded key.ProjectPath)
            | Some kv -> getPo kv

        and getPo (kv: KeyValuePair<ProjectKey, ProjectOptions>) : Result<FCSProjectOptionsData, GetProjectOptionsErrors> =
          match kv.Value with
          | po when not (po.ProjectFileName.EndsWith(".fsproj")) ->
              Error (LanguageNotSupported po.ProjectFileName)
          | po ->

              let isGeneratedTfmAssemblyInfoFile (path: string) =
                let f = System.IO.Path.GetFileName(path)
                f.StartsWith(".NETFramework,Version=v") && f.EndsWith(".AssemblyAttributes.fs")

              let refs =
                po.ReferencedProjects
                |> List.map (fun key ->
                  getPoByKey { ProjectKey.ProjectPath = key.ProjectFileName; TargetFramework = key.TargetFramework }
                  |> Result.bind (fun (po: FCSProjectOptionsData) ->
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

                let fcsPoData : FCSProjectOptionsData = {
                    FCSProjectOptionsData.ProjectId = None
                    ProjectFileName = po.ProjectFileName
                    SourceFiles =
                        po.SourceFiles
                        |> List.filter (fun p -> not (isGeneratedTfmAssemblyInfoFile p))
                        |> Array.ofList
                    OtherOptions =
                        po.OtherOptions
                        |> List.filter (fun n -> not (FscArguments.isDeprecatedArg n))
                        |> Array.ofList
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
                // TODO check use full paths?

                Ok fcsPoData

        let byPath path (kv: KeyValuePair<ProjectKey, ProjectOptions>) =
          if kv.Key.ProjectPath = path then
            Some kv
          else
            None

        match parsed |> Array.tryPick (byPath path) with
        | Some po ->
            getPo po
            |> Result.map toFCSPoMapper
        | None ->
          match workspace.LastError path with
          | Some e -> Error e
          | None -> Error (ProjectNotLoaded path)
