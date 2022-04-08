namespace Ionide.ProjInfo

open System
open System.IO
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
            |> Array.choose (fun d ->
                let knownProject = allKnownProjects |> Seq.tryFind (fun n -> n.ProjectFileName = d.ProjectFileName)

                let isDotnetProject (knownProject: ProjectOptions option) =
                    match knownProject with
                    | Some p -> (p.ProjectFileName.EndsWith(".csproj") || p.ProjectFileName.EndsWith(".vbproj")) && File.Exists p.TargetPath
                    | None -> false

                if d.ProjectFileName.EndsWith ".fsproj" then
                    knownProject
                    |> Option.map (fun p -> FSharpReferencedProject.CreateFSharp(p.TargetPath, mapToFSharpProjectOptions p allKnownProjects))
                elif isDotnetProject knownProject then
                    knownProject
                    |> Option.map (fun p -> FSharpReferencedProject.CreatePortableExecutable(p.TargetPath, (fun () -> DateTime.Now), (fun _ -> None)))
                else
                    None)
          IsIncompleteTypeCheckEnvironment = false
          UseScriptResolutionRules = false
          LoadTime = projectOptions.LoadTime
          UnresolvedReferences = None // it's always None
          OriginalLoadReferences = [] // it's always empty list
          Stamp = None }
