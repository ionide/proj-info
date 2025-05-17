namespace Ionide.ProjInfo.ProjectSystem

open System
open System.IO
open Ionide.ProjInfo
open Ionide.ProjInfo.Logging

module WorkspacePeek =

    let rec logger = LogProvider.getLoggerByQuotation <@ logger @>

    [<RequireQualifiedAccess>]
    type Interesting =
        | Solution of string * InspectSln.SolutionData
        | Directory of string * string list

    open System.IO

    type private UsefulFile =
        | FsProj
        | Sln
        | Slnf
        | Slnx
        | Fsx

    [<return: Struct>]
    let inline (|HasExt|_|) (ext: string) (file: FileInfo) =
        if file.Extension = ext then
            ValueSome()
        else
            ValueNone

    let private partitionByChoice3 =
        let foldBy (a, b, c) t =
            match t with
            | Choice1Of3 x -> (x :: a, b, c)
            | Choice2Of3 x -> (a, x :: b, c)
            | Choice3Of3 x -> (a, b, x :: c)

        Array.fold foldBy ([], [], [])

    let peek (rootDir: string) deep (excludedDirs: string list) =
        if isNull rootDir then
            []
        else
            let dirInfo = DirectoryInfo(rootDir)

            //TODO accept glob list to ignore
            let ignored =
                let normalizedDirs =
                    excludedDirs
                    |> List.map (fun s -> s.ToUpperInvariant())
                    |> Array.ofList

                (fun (s: string) ->
                    normalizedDirs
                    |> Array.contains (s.ToUpperInvariant())
                )

            let scanDir (dirInfo: DirectoryInfo) =
                let hasExt (ext: string) (s: FileInfo) = s.FullName.EndsWith(ext)

                let topLevelFiles =
                    try
                        dirInfo.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                    with
                    | :? UnauthorizedAccessException as ex ->
                        logger.error (
                            Log.setMessage "Unauthorized access error while reading files of {dir}"
                            >> Log.addContextDestructured "dir" dirInfo.Name
                            >> Log.addExn ex
                        )

                        Seq.empty
                    | ex ->
                        logger.error (
                            Log.setMessage "Failed to read files of {dir}"
                            >> Log.addContextDestructured "dir" dirInfo.Name
                            >> Log.addExn ex
                        )

                        Seq.empty

                topLevelFiles
                |> Seq.choose (fun s ->
                    match s with
                    | HasExt ".sln" -> Some(UsefulFile.Sln, s)
                    | HasExt ".slnf" -> Some(UsefulFile.Slnf, s)
                    | HasExt ".fsx" -> Some(UsefulFile.Fsx, s)
                    | HasExt ".fsproj" -> Some(UsefulFile.FsProj, s)
                    | HasExt ".slnx" -> Some(UsefulFile.Slnx, s)
                    | _ -> None
                )
                |> Seq.toArray

            let dirs =
                let getDirectories (dirInfo: DirectoryInfo) =
                    try
                        dirInfo.GetDirectories()
                    with
                    | :? UnauthorizedAccessException as ex ->
                        logger.error (
                            Log.setMessage "Unauthorized access error while reading sub directories of {dir}"
                            >> Log.addContextDestructured "dir" dirInfo.Name
                            >> Log.addExn ex
                        )

                        Array.empty
                    | ex ->
                        logger.error (
                            Log.setMessage "Failed to read sub directories of {dir}"
                            >> Log.addContextDestructured "dir" dirInfo.Name
                            >> Log.addExn ex
                        )

                        Array.empty

                let rec scanDirs (dirInfo: DirectoryInfo) lvl =
                    seq {
                        if
                            lvl
                            <= deep
                        then
                            yield dirInfo

                            for s in getDirectories dirInfo do
                                if not (ignored s.Name) then
                                    yield! scanDirs s (lvl + 1)
                    }

                scanDirs dirInfo 0
                |> Array.ofSeq

            let getInfo (t, (f: FileInfo)) =
                match t with
                | UsefulFile.Sln
                | UsefulFile.Slnf
                | UsefulFile.Slnx ->
                    match InspectSln.tryParseSln f.FullName with
                    | Ok(d) -> Some(Choice1Of3(f.FullName, d))
                    | Error e ->
                        logger.warn (
                            Log.setMessage "Failed to load file: {filePath} : {data}"
                            >> Log.addContext "filePath" f.FullName
                            >> Log.addExn e
                        )

                        None
                | UsefulFile.Fsx -> Some(Choice2Of3(f.FullName))
                | UsefulFile.FsProj -> Some(Choice3Of3(f.FullName))

            let found =
                dirs
                |> Array.Parallel.collect scanDir
                |> Array.Parallel.choose getInfo

            let slns, _fsxs, fsprojs =
                found
                |> partitionByChoice3

            //TODO weight order of fsprojs from sln
            let dir =
                rootDir,
                (fsprojs
                 |> List.sort)

            [
                yield!
                    slns
                    |> List.map Interesting.Solution
                yield
                    dir
                    |> Interesting.Directory
            ]
