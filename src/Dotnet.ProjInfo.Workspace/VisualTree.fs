namespace Dotnet.ProjInfo.Workspace

module VisualTree =

    open System
    open System.IO
    open Dotnet.ProjInfo.Inspect

    // let f1 = @"c:\prova\src\a\a.fs";;
    // let f2 = @"c:\prova\src\b\b.fs";;
    // let fsproj = @"c:\prova\src\a\a.fsproj";;

    let getDirEnsureTrailingSlash projPath =
        let dir = Path.GetDirectoryName(projPath)
        if dir.EndsWith(Path.DirectorySeparatorChar.ToString()) then
            dir
        else
            dir + Path.DirectorySeparatorChar.ToString()

    let relativePathOf fromPath toPath =
        let fromUri = Uri(fromPath)
        let toUri = Uri(toPath)
        fromUri.MakeRelativeUri(toUri).OriginalString

    let relativeToProjDir projPath filePath =
        filePath |> relativePathOf (getDirEnsureTrailingSlash projPath)

    let visualPathVSBehaviour projPath filePath =
        let relativePath = filePath |> relativeToProjDir projPath
        if relativePath.StartsWith("..") then
            //if is not a child of proj directory, VS show only the name of the file
            Path.GetFileName(relativePath)
        else
            relativePath

    let getVisualPath linkMetadata fullpathMetadata identity projPath =
        match linkMetadata, fullpathMetadata with
        | None, None ->
            //TODO fullpath was expected, something is wrong. log it
            identity, identity
        | Some l, None ->
            //TODO fullpath was expected, something is wrong. log it
            l, identity
        | None, Some path ->
            //TODO if is not contained in project dir, just show name, to
            //behave like VS
            let relativeToPrjDir = path |> visualPathVSBehaviour projPath
            relativeToPrjDir, path
        | Some l, Some path ->
            l, path

    let getProjectItem projPath (p: GetItemResult) : ProjectItem =
        let tryFindMetadata modifier =
            p.Metadata
            |> List.tryFind (fun (m, _) -> m = modifier)
            |> Option.map snd

        let linkMetadata = tryFindMetadata (GetItemsModifier.Custom("Link"))
        let fullpathMetadata = tryFindMetadata (GetItemsModifier.FullPath)

        let projDir = Path.GetDirectoryName(projPath)

        let (name, fullpath) = projDir |> getVisualPath linkMetadata fullpath p.Identity 

        ProjectItem.Compile (name, fullpath)

