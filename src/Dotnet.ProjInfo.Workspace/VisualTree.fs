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
        let normalizeLink (linkPath: string) =
            // always use / as path separator for link, regardless of OS, because is a virtual path
            linkPath.Replace('\\','/')

        match linkMetadata, fullpathMetadata with
        | Some "", None
        | None, None ->
            //TODO fullpath was expected, something is wrong. log it
            identity, identity
        | Some l, None ->
            //TODO fullpath was expected, something is wrong. log it
            (normalizeLink l), identity
        | Some "", Some path
        | None, Some path ->
            //TODO if is not contained in project dir, just show name, to
            //behave like VS
            let relativeToPrjDir = path |> visualPathVSBehaviour projPath
            relativeToPrjDir, path
        | Some l, Some path ->
            (normalizeLink l), path

    let tryFindMetadata modifier p =
        p.Metadata
        |> List.tryFind (fun (m, _) -> m = modifier)
        |> Option.map snd

    let getCompileProjectItem (projItems: GetItemResult list) projPath sourceFile =

        let isCompileItemWithFullpath fullpath (p: GetItemResult) =
            if p.Name = "Compile" then
                match p |> tryFindMetadata (GetItemsModifier.FullPath) with
                | None -> false
                | Some fullpathMetadata -> fullpathMetadata = fullpath
            else
                false

        let item = projItems |> List.tryFind (isCompileItemWithFullpath sourceFile)
        match item with
        | None -> 
            let (name, fullpath) = projPath |> getVisualPath None (Some sourceFile) sourceFile

            ProjectItem.Compile (name, fullpath)
        | Some p ->
            let linkMetadata = p |> tryFindMetadata (GetItemsModifier.Custom("Link"))
            let fullpathMetadata = p |> tryFindMetadata (GetItemsModifier.FullPath)

            let (name, fullpath) = projPath |> getVisualPath linkMetadata fullpathMetadata p.Identity 

            ProjectItem.Compile (name, fullpath)
