namespace Dotnet.ProjInfo

open Dotnet.ProjInfo.Types
open FSharp.Compiler.SourceCodeServices

module FCS =
    let rec mapToFSharpProjectOptions (projectOptions: ProjectOptions) (allKnownProjects: ProjectOptions seq): FSharpProjectOptions =
        { ProjectId = projectOptions.ProjectId
          ProjectFileName = projectOptions.ProjectFileName
          SourceFiles = List.toArray projectOptions.SourceFiles
          OtherOptions = List.toArray projectOptions.OtherOptions
          ReferencedProjects =
              projectOptions.ReferencedProjects
              |> List.toArray
              |> Array.choose
                  (fun d ->
                      let findProjOpt = allKnownProjects |> Seq.tryFind (fun n -> n.ProjectFileName = d.ProjectFileName)
                      findProjOpt |> Option.map (fun p -> p.ProjectFileName, (mapToFSharpProjectOptions p allKnownProjects)))
          IsIncompleteTypeCheckEnvironment = false
          UseScriptResolutionRules = false
          LoadTime = projectOptions.LoadTime
          UnresolvedReferences = None // it's always None
          OriginalLoadReferences = [] // it's always empty list
          Stamp = None
          ExtraProjectInfo = Some(box projectOptions) }
