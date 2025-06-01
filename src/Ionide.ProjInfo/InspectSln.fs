namespace Ionide.ProjInfo

module InspectSln =

    open System
    open System.IO
    open System.Threading
    open Microsoft.VisualStudio.SolutionPersistence
    open Microsoft.VisualStudio.SolutionPersistence.Serializer
    open System.Text.Json

    let private normalizeDirSeparators (path: string) =
        match Path.DirectorySeparatorChar with
        | '\\' -> path.Replace('/', '\\')
        | '/' -> path.Replace('\\', '/')
        | _ -> path

    type SolutionData = {
        Items: SolutionItem list
        Configurations: SolutionConfiguration list
    }

    and SolutionConfiguration = {
        Id: string
        ConfigurationName: string
        PlatformName: string
        IncludeInBuild: bool
    }

    and SolutionItem = {
        Guid: Guid
        Name: string
        Kind: SolutionItemKind
    }

    and SolutionItemKind =
        | MSBuildFormat of SolutionItemMSBuildConfiguration list
        | Folder of (SolutionItem list) * (string list)
        | Unsupported
        | Unknown

    and SolutionItemMSBuildConfiguration = {
        Id: string
        ConfigurationName: string
        PlatformName: string
    }

    let private tryLoadSolutionModel (slnFilePath: string) =
        // use the VS library to parse the solution
        match SolutionSerializers.GetSerializerByMoniker(slnFilePath) with
        | null -> Error(exn $"Unsupported solution file format %s{Path.GetExtension(slnFilePath)}")
        | serializer ->
            try
                let model = serializer.OpenAsync(slnFilePath, CancellationToken.None).GetAwaiter().GetResult()
                Ok(model)
            with ex ->
                Error ex

    /// Parses a file on disk and returns data about its contents. Supports sln, slnf, and slnx files.
    let tryParseSln (slnFilePath: string) =

        let slnDir = Path.GetDirectoryName slnFilePath

        let makeAbsoluteFromSlnDir =
            let makeAbs (path: string) =
                if Path.IsPathRooted path then
                    path
                else
                    Path.Combine(slnDir, path)
                    |> Path.GetFullPath

            normalizeDirSeparators
            >> makeAbs

        let parseSln (sln: Model.SolutionModel) (projectsToRead: string Set option) =
            sln.DistillProjectConfigurations()

            let parseItem (item: Model.SolutionItemModel) : SolutionItem = {
                Guid = item.Id
                Name = ""
                Kind = SolutionItemKind.Unknown
            }

            let parseProject (project: Model.SolutionProjectModel) : SolutionItem = {
                Guid = project.Id
                Name = makeAbsoluteFromSlnDir project.FilePath
                Kind = SolutionItemKind.MSBuildFormat [] // TODO: could theoretically parse configurations here
            }

            let parseFolder (folder: Model.SolutionFolderModel) : SolutionItem = {
                Guid = folder.Id
                Name = folder.ActualDisplayName
                Kind =
                    SolutionItemKind.Folder(
                        sln.SolutionItems
                        |> Seq.filter (fun item ->
                            not (isNull item.Parent)
                            && item.Parent.Id = folder.Id
                        )
                        |> Seq.map (fun p -> parseItem p)
                        |> List.ofSeq,

                        folder.Files
                        |> Option.ofObj
                        |> Option.map (
                            Seq.map makeAbsoluteFromSlnDir
                            >> List.ofSeq
                        )
                        |> Option.defaultValue []
                    )
            }

            // three kinds of items - projects, folders, items
            // yield them all here
            let projectsWeCareAbout =
                match projectsToRead with
                | None -> sln.SolutionProjects :> seq<_>
                | Some filteredProjects ->
                    sln.SolutionProjects
                    |> Seq.filter (fun slnProject -> filteredProjects.Contains(makeAbsoluteFromSlnDir slnProject.FilePath))

            let allItems = [
                yield!
                    projectsWeCareAbout
                    |> Seq.map parseProject
                yield!
                    sln.SolutionFolders
                    |> Seq.map parseFolder
                yield!
                    sln.SolutionItems
                    |> Seq.filter (fun item -> isNull item.Parent)
                    |> Seq.map parseItem
            ]

            let data = {
                Items = allItems
                Configurations = []
            }

            data

        let parseSlnf (slnfPath: string) =
            let (slnFilePath: string, projectsToRead: string Set) =
                let options = new JsonDocumentOptions(AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip)
                let text = JsonDocument.Parse(File.ReadAllText(slnfPath), options)
                let solutionElement = text.RootElement.GetProperty("solution")
                let slnPath = solutionElement.GetProperty("path").GetString()

                let projects =
                    solutionElement.GetProperty("projects").EnumerateArray()
                    |> Seq.map (fun p -> p.GetString())
                    |> Seq.map makeAbsoluteFromSlnDir
                    |> Set.ofSeq

                makeAbsoluteFromSlnDir slnPath, projects

            match tryLoadSolutionModel slnFilePath with
            | Ok sln -> Ok(parseSln sln (Some projectsToRead))
            | Error ex -> Error ex

        if slnFilePath.EndsWith(".slnf") then
            parseSlnf slnFilePath
        else
            match tryLoadSolutionModel slnFilePath with
            | Ok sln -> Ok(parseSln sln None)
            | Error ex -> Error ex

    let loadingBuildOrder (data: SolutionData) =

        let rec projs (item: SolutionItem) =
            match item.Kind with
            | MSBuildFormat items -> [ item.Name ]
            | Folder(items, _) ->
                items
                |> List.collect projs
            | Unsupported
            | Unknown -> []

        data.Items
        |> List.collect projs
