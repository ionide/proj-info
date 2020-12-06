namespace Dotnet.ProjInfo

module internal CommonHelpers =

    let chooseByPrefix (prefix: string) (s: string) =
        if s.StartsWith(prefix) then Some (s.Substring(prefix.Length))
        else None

    let chooseByPrefix2 prefixes (s: string) =
        prefixes
        |> List.tryPick (fun prefix -> chooseByPrefix prefix s)

    let splitByPrefix (prefix: string) (s: string) =
        if s.StartsWith(prefix) then Some (prefix, s.Substring(prefix.Length))
        else None

    let splitByPrefix2 prefixes (s: string) =
        prefixes
        |> List.tryPick (fun prefix -> splitByPrefix prefix s)

module internal FscArguments =

    open CommonHelpers
    open Types
    open System.IO

    let outType rsp =
        match List.tryPick (chooseByPrefix "--target:") rsp with
        | Some "library" -> ProjectOutputType.Library
        | Some "exe" -> ProjectOutputType.Exe
        | Some v -> ProjectOutputType.Custom v
        | None -> ProjectOutputType.Exe // default if arg is not passed to fsc

    let private outputFileArg = ["--out:"; "-o:"]

    let private makeAbs (projDir: string) (f: string) =
        if Path.IsPathRooted f then f else Path.Combine(projDir, f)

    let outputFile projDir rsp =
        rsp
        |> List.tryPick (chooseByPrefix2 outputFileArg)
        |> Option.map (makeAbs projDir)

    let isCompileFile (s:string) =
        //TODO check if is not an option, check prefix `-` ?
        s.EndsWith(".fs") || s.EndsWith (".fsi") || s.EndsWith (".fsx")

    let references =
        //TODO valid also --reference:
        List.choose (chooseByPrefix "-r:")

    let useFullPaths projDir (s: string) =
        match s |> splitByPrefix2 outputFileArg with
        | Some (prefix, v) ->
            prefix + (v |> makeAbs projDir)
        | None ->
            if isCompileFile s then
                s |> makeAbs projDir |> Path.GetFullPath
            else
                s

    let isTempFile (name: string) =
        let tempPath = System.IO.Path.GetTempPath()
        let s = name.ToLower()
        s.StartsWith(tempPath.ToLower())

    let isDeprecatedArg n =
        // TODO put in FCS
        (n = "--times") || (n = "--no-jit-optimize")

    let isSourceFile (file: string) : (string -> bool) =
        if System.IO.Path.GetExtension(file) = ".fsproj" then
            isCompileFile
        else
            (fun n -> n.EndsWith ".cs")
