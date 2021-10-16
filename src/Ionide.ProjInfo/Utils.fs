namespace Ionide.ProjInfo

open System.Runtime.InteropServices
open System.IO
open System

module Paths =
    let private isUnix = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

    let private dotnetBinaryName =
        if isUnix then
            "dotnet"
        else
            "dotnet.exe"

    let private potentialDotnetHostEnvVars =
        [ "DOTNET_HOST_PATH", id // is a full path to dotnet binary
          "DOTNET_ROOT", (fun s -> Path.Combine(s, dotnetBinaryName)) // needs dotnet binary appended
          "DOTNET_ROOT(x86)", (fun s -> Path.Combine(s, dotnetBinaryName)) ] // needs dotnet binary appended

    let private existingEnvVarValue envVarValue =
        match envVarValue with
        | null
        | "" -> None
        | other -> Some other

    /// <summary>
    /// provides the path to the `dotnet` binary running this library, respecting various dotnet <see href="https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_root-dotnet_rootx86%5D">environment variables</see>
    /// </summary>
    let dotnetRoot =
        potentialDotnetHostEnvVars
        |> List.tryPick
            (fun (envVar, transformer) ->
                match Environment.GetEnvironmentVariable envVar |> existingEnvVarValue with
                | Some varValue -> Some(transformer varValue)
                | None -> None)
        |> Option.defaultWith
            (fun _ ->
                System
                    .Diagnostics
                    .Process
                    .GetCurrentProcess()
                    .MainModule
                    .FileName)

    let sdksPath (dotnetRoot: string) =
        System.IO.Path.Combine(dotnetRoot, "Sdks")

module internal CommonHelpers =

    let chooseByPrefix (prefix: string) (s: string) =
        if s.StartsWith(prefix) then
            Some(s.Substring(prefix.Length))
        else
            None

    let chooseByPrefix2 prefixes (s: string) =
        prefixes |> List.tryPick (fun prefix -> chooseByPrefix prefix s)

    let splitByPrefix (prefix: string) (s: string) =
        if s.StartsWith(prefix) then
            Some(prefix, s.Substring(prefix.Length))
        else
            None

    let splitByPrefix2 prefixes (s: string) =
        prefixes |> List.tryPick (fun prefix -> splitByPrefix prefix s)

module FscArguments =

    open CommonHelpers
    open Types
    open System.IO

    let outType rsp =
        match List.tryPick (chooseByPrefix "--target:") rsp with
        | Some "library" -> ProjectOutputType.Library
        | Some "exe" -> ProjectOutputType.Exe
        | Some v -> ProjectOutputType.Custom v
        | None -> ProjectOutputType.Exe // default if arg is not passed to fsc

    let private outputFileArg = [ "--out:"; "-o:" ]

    let private makeAbs (projDir: string) (f: string) =
        if Path.IsPathRooted f then
            f
        else
            Path.Combine(projDir, f)

    let outputFile projDir rsp =
        rsp |> List.tryPick (chooseByPrefix2 outputFileArg) |> Option.map (makeAbs projDir)

    let isCompileFile (s: string) =
        let isArg = s.StartsWith("-") && s.Contains(":")
        (not isArg) && (s.EndsWith(".fs") || s.EndsWith(".fsi") || s.EndsWith(".fsx"))

    let references =
        //TODO valid also --reference:
        List.choose (chooseByPrefix "-r:")

    let useFullPaths projDir (s: string) =
        match s |> splitByPrefix2 outputFileArg with
        | Some (prefix, v) -> prefix + (v |> makeAbs projDir)
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

module CscArguments =
    open CommonHelpers
    open System.IO
    open Types

    let private outputFileArg = [ "--out:"; "-o:" ]

    let private makeAbs (projDir: string) (f: string) =
        if Path.IsPathRooted f then
            f
        else
            Path.Combine(projDir, f)

    let isCompileFile (s: string) =
        let isArg = s.StartsWith("-") && s.Contains(":")
        (not isArg) && s.EndsWith(".cs")

    let useFullPaths projDir (s: string) =
        if isCompileFile s then
            s |> makeAbs projDir |> Path.GetFullPath
        else
            s

    let isSourceFile (file: string) : (string -> bool) =
        if System.IO.Path.GetExtension(file) = ".csproj" then
            isCompileFile
        else
            (fun n -> n.EndsWith ".fs")

    let outputFile projDir rsp =
        rsp |> List.tryPick (chooseByPrefix2 outputFileArg) |> Option.map (makeAbs projDir)

    let outType rsp =
        match List.tryPick (chooseByPrefix "/target:") rsp with
        | Some "library" -> ProjectOutputType.Library
        | Some "exe" -> ProjectOutputType.Exe
        | Some v -> ProjectOutputType.Custom v
        | None -> ProjectOutputType.Exe // default if arg is not passed to fsc
