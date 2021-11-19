namespace Ionide.ProjInfo

open Ionide.ProjInfo.Types
open FSharp.Compiler.CodeAnalysis

module FCS =
    let rec mapToFSharpProjectOptions (projectOptions: ProjectOptions) (allKnownProjects: ProjectOptions seq) : FSharpProjectOptions =
        { ProjectId = None
          ProjectFileName = projectOptions.ProjectFileName
          SourceFiles = List.toArray projectOptions.SourceFiles
          OtherOptions = List.toArray projectOptions.OtherOptions
          ReferencedProjects =
              projectOptions.ReferencedProjects
              |> List.toArray
              |> Array.choose
                  (fun d ->
                      if d.ProjectFileName.EndsWith ".fsproj" then
                          allKnownProjects
                          |> Seq.tryFind (fun n -> n.ProjectFileName = d.ProjectFileName)
                          |> Option.map (fun p -> FSharpReferencedProject.CreateFSharp(p.TargetPath, mapToFSharpProjectOptions p allKnownProjects))
                      else
                          // TODO: map other project types to references here
                          None)
          IsIncompleteTypeCheckEnvironment = false
          UseScriptResolutionRules = false
          LoadTime = projectOptions.LoadTime
          UnresolvedReferences = None // it's always None
          OriginalLoadReferences = [] // it's always empty list
          Stamp = None }
