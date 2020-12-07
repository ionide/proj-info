namespace Dotnet.ProjInfo.ProjectSystem

open System
open System.IO
open Dotnet.ProjInfo

module WorkspacePeek =


    [<RequireQualifiedAccess>]
    type Interesting =
        | Solution of string * InspectSln.SolutionData
        | Directory of string * string list

    open System.IO

    type private UsefulFile =
        | FsProj
        | Sln
        | Fsx

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
                let normalizedDirs = excludedDirs |> List.map (fun s -> s.ToUpperInvariant()) |> Array.ofList
                (fun (s: string) -> normalizedDirs |> Array.contains (s.ToUpperInvariant()))

            let scanDir (dirInfo: DirectoryInfo) =
                let hasExt (ext: string) (s: FileInfo) = s.FullName.EndsWith(ext)

                dirInfo.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                |> Seq.choose
                    (fun s ->
                        match s with
                        | x when x |> hasExt ".sln" -> Some(UsefulFile.Sln, x)
                        | x when x |> hasExt ".fsx" -> Some(UsefulFile.Fsx, x)
                        | x when x |> hasExt ".fsproj" -> Some(UsefulFile.FsProj, x)
                        | _ -> None)
                |> Seq.toArray

            let dirs =
                let rec scanDirs (dirInfo: DirectoryInfo) lvl =
                    seq {
                        if lvl <= deep then
                            yield dirInfo

                            for s in dirInfo.GetDirectories() do
                                if not (ignored s.Name)
                                then yield! scanDirs s (lvl + 1)
                    }

                scanDirs dirInfo 0 |> Array.ofSeq

            let getInfo (t, (f: FileInfo)) =
                match t with
                | UsefulFile.Sln ->
                    match InspectSln.tryParseSln f.FullName with
                    | Ok (p, d) -> Some(Choice1Of3(p, d))
                    | _ -> None
                | UsefulFile.Fsx -> Some(Choice2Of3(f.FullName))
                | UsefulFile.FsProj -> Some(Choice3Of3(f.FullName))

            let found = dirs |> Array.Parallel.collect scanDir |> Array.Parallel.choose getInfo

            let slns, _fsxs, fsprojs = found |> partitionByChoice3

            //TODO weight order of fsprojs from sln
            let dir = rootDir, (fsprojs |> List.sort)

            [ yield! slns |> List.map Interesting.Solution
              yield dir |> Interesting.Directory ]
