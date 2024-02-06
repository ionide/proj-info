namespace Ionide.ProjInfo

open System
open System.IO
open Ionide.ProjInfo.Types
open FSharp.Compiler.CodeAnalysis

module FCS =
    let private loadFromDotnetDll (p: ProjectOptions) =
        /// because only a successful compilation will be written to a DLL, we can rely on
        /// the file metadata for things like write times
        let projectFile = FileInfo p.TargetPath

        let getStamp () = projectFile.LastWriteTimeUtc

        let getStream (ctok: System.Threading.CancellationToken) =
            try
                projectFile.OpenRead() :> Stream
                |> Some
            with _ ->
                None

        let delayedReader = DelayedILModuleReader(p.TargetPath, getStream)

        FSharpReferencedProject.PEReference(getStamp, delayedReader)

    let private makeFCSOptions mapProjectToReference (project: ProjectOptions) = {
        ProjectId = None
        ProjectFileName = project.ProjectFileName
        SourceFiles = List.toArray project.SourceFiles
        OtherOptions = List.toArray project.OtherOptions
        ReferencedProjects =
            project.ReferencedProjects
            |> List.toArray
            |> Array.choose mapProjectToReference
        IsIncompleteTypeCheckEnvironment = false
        UseScriptResolutionRules = false
        LoadTime = project.LoadTime
        UnresolvedReferences = None // it's always None
        OriginalLoadReferences = [] // it's always empty list
        Stamp = None
    }

    let rec private makeProjectReference isKnownProject makeFSharpProjectReference (p: ProjectReference) : FSharpReferencedProject option =
        let knownProject = isKnownProject p

        let isDotnetProject (knownProject: ProjectOptions option) =
            match knownProject with
            | Some p ->
                (p.ProjectFileName.EndsWith(".csproj")
                 || p.ProjectFileName.EndsWith(".vbproj"))
                && File.Exists p.ResolvedTargetPath
            | None -> false

        if p.ProjectFileName.EndsWith ".fsproj" then
            knownProject
            |> Option.map (fun (p: ProjectOptions) ->
                let theseOptions = makeFSharpProjectReference p
                FSharpReferencedProject.FSharpReference(p.ResolvedTargetPath, theseOptions)
            )
        elif isDotnetProject knownProject then
            knownProject
            |> Option.map loadFromDotnetDll
        else
            None

    let mapManyOptions (allKnownProjects: ProjectOptions seq) : FSharpProjectOptions seq =
        seq {
            let dict = System.Collections.Concurrent.ConcurrentDictionary<ProjectOptions, FSharpProjectOptions>()

            let isKnownProject (p: ProjectReference) =
                allKnownProjects
                |> Seq.tryFind (fun kp -> kp.ProjectFileName = p.ProjectFileName)

            let rec makeFSharpProjectReference (p: ProjectOptions) =
                let factory = makeProjectReference isKnownProject makeFSharpProjectReference
                dict.GetOrAdd(p, (fun p -> makeFCSOptions factory p))

            for project in allKnownProjects do
                let thisProject =
                    dict.GetOrAdd(project, (fun p -> makeFCSOptions (makeProjectReference isKnownProject makeFSharpProjectReference) p))

                yield thisProject
        }

    let rec mapToFSharpProjectOptions (projectOptions: ProjectOptions) (allKnownProjects: ProjectOptions seq) : FSharpProjectOptions =
        let isKnownProject (d: ProjectReference) =
            allKnownProjects
            |> Seq.tryFind (fun n -> n.ProjectFileName = d.ProjectFileName)

        makeFCSOptions (makeProjectReference isKnownProject (fun p -> mapToFSharpProjectOptions p allKnownProjects)) projectOptions
