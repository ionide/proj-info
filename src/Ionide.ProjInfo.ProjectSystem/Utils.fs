namespace Ionide.ProjInfo.ProjectSystem

open System.IO
open System.Collections.Concurrent

[<AutoOpen>]
module internal Utils =
    let inline combinePaths path1 (path2: string) =
        Path.Combine(path1, path2.TrimStart [| '\\'; '/' |])

    let inline (</>) path1 path2 = combinePaths path1 path2

    let chooseByPrefix (prefix: string) (s: string) =
        if s.StartsWith(prefix) then
            Some(s.Substring(prefix.Length))
        else
            None

    let normalizeDirSeparators (path: string) =
        match Path.DirectorySeparatorChar with
        | '\\' -> path.Replace('/', '\\')
        | '/' -> path.Replace('\\', '/')
        | _ -> path

    let normalizePath (file: string) =
        if file.EndsWith ".fs" || file.EndsWith ".fsi" then
            let p = Path.GetFullPath file
            (p.Chars 0).ToString().ToLower() + p.Substring(1)
        else
            file

    type ConcurrentDictionary<'key, 'value> with
        member x.TryFind key =
            match x.TryGetValue key with
            | true, value -> Some value
            | _ -> None

    [<RequireQualifiedAccess>]
    module Async =
        /// Transforms an Async value using the specified function.
        [<CompiledName("Map")>]
        let map (mapping: 'a -> 'b) (value: Async<'a>) : Async<'b> =
            async {
                // Get the input value.
                let! x = value
                // Apply the mapping function and return the result.
                return mapping x
            }
