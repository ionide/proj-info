/// Functions to retrieve .net framework and .net core dependencies
/// for resolution of FSI

[<RequireQualifiedAccess>]
module Ionide.ProjInfo.ProjectSystem.FSIRefs

open System
open System.IO
open System.Runtime.InteropServices
open SemanticVersioning

let defaultDotNetSDKRoot =
    let path =
        if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            "/usr/local/share/dotnet"
        else if RuntimeInformation.IsOSPlatform OSPlatform.Linux then
            "/usr/share/dotnet"
        else
            @"C:\Program Files\dotnet"

    DirectoryInfo path

type TFM =
    | NetFx
    | NetCore

let versionDirectoriesIn (baseDir: DirectoryInfo) =
    baseDir.EnumerateDirectories()
    |> Array.ofSeq // have to convert to array to get a sortWith that takes our custom comparison function
    |> Array.choose (fun dir ->
        match Version.TryParse dir.Name with
        | true, v -> Some v
        | false, _ -> None
    )
    |> Array.sort

/// path to the directory where .Net SDK versions are stored
let sdkDir (dotnetRoot: DirectoryInfo) =
    Path.Combine(dotnetRoot.FullName, "sdk")
    |> DirectoryInfo

/// returns a sorted list of the SDK versions available at the given dotnet root
let sdkVersions dotnetRoot =
    let sdkDir = sdkDir dotnetRoot

    if sdkDir.Exists then
        Some(versionDirectoriesIn sdkDir)
    else
        None

/// path to the .netcoreapp reference assembly storage location
let netcoreAppPacksDir (dotnetRoot: DirectoryInfo) =
    Path.Combine(dotnetRoot.FullName, "packs/Microsoft.NETCore.App.Ref")
    |> DirectoryInfo

/// path to the .netcoreapp implementation assembly storage location
let netcoreAppDir (dotnetRoot: DirectoryInfo) =
    Path.Combine(dotnetRoot.FullName, "shared/Microsoft.NETCore.App")
    |> DirectoryInfo

/// Returns a sorted list of the .Net Core runtime versions available at the given dotnet root.
///
/// If the reference-dll packs directory ('dotnet root'/packs/Microsoft.NETCore.App.Ref) is present that is used, otherwise
/// defaults to the actual runtime implementation dlls ('dotnet root'/shared/Microsoft.NETCore.app).
let runtimeVersions (dotnetRoot: DirectoryInfo) =
    let runtimesDir = netcoreAppDir dotnetRoot
    let packsDir = netcoreAppPacksDir dotnetRoot

    if packsDir.Exists then
        Some(versionDirectoriesIn packsDir)
    else if runtimesDir.Exists then
        Some(versionDirectoriesIn runtimesDir)
    else
        None

let appPackDir dotnetRoot runtimeVersion tfm =
    let packDir = Path.Combine((netcoreAppPacksDir dotnetRoot).FullName, runtimeVersion, "ref", tfm)

    if Directory.Exists packDir then
        Some(DirectoryInfo packDir)
    else
        None

let compilerDir dotnetRoot sdkVersion =
    let compilerDir = Path.Combine((sdkDir dotnetRoot).FullName, sdkVersion, "FSharp")

    if Directory.Exists compilerDir then
        Some(DirectoryInfo compilerDir)
    else
        None

let runtimeDir dotnetRoot runtimeVersion =
    let runtimeDir = Path.Combine((netcoreAppDir dotnetRoot).FullName, runtimeVersion)

    if Directory.Exists runtimeDir then
        Some(DirectoryInfo runtimeDir)
    else
        None

/// given the pack directory and the runtime directory, we prefer the pack directory (because these are ref dlls)
/// but will accept the runtime dir if no pack exists.
let findRuntimeRefs packDir runtimeDir =
    match packDir, runtimeDir with
    | Some(refDir: DirectoryInfo), _
    | _, Some(refDir: DirectoryInfo) ->
        refDir.EnumerateFiles()
        // SUPER IMPORTANT: netstandard/netcore assembly resolution _must not_ contain mscorlib or else
        // its presence triggers old netfx fallbacks, which end up bringing assemblies that aren't part
        // of netcore.
        |> Seq.choose (fun r ->
            if
                r.Extension.EndsWith "dll"
                && not (Path.GetFileNameWithoutExtension(r.Name) = "mscorlib")
            then
                Some r.FullName
            else
                None
        )
        |> Seq.toArray
    | None, None -> [||]

/// given the compiler root dir and if to include FSI refs, returns the set of compiler assemblies to references if that dir exists.
let compilerAndInteractiveRefs compilerDir useFsiAuxLib =
    match compilerDir with
    | Some(dir: DirectoryInfo) -> [
        yield Path.Combine(dir.FullName, "FSharp.Core.dll")
        if useFsiAuxLib then
            yield Path.Combine(dir.FullName, "FSharp.Compiler.Interactive.Settings.dll")
      ]
    | None -> []

/// refs for .net core include:
/// * runtime-required refs (either ref façades if available or full assemblies if no ref façades are present), and
/// * compiler/fsi-required refs
let netCoreRefs dotnetRoot sdkVersion runtimeVersion tfm useFsiAuxLib = [
    yield! findRuntimeRefs (appPackDir dotnetRoot runtimeVersion tfm) (runtimeDir dotnetRoot runtimeVersion)
    yield! compilerAndInteractiveRefs (compilerDir dotnetRoot sdkVersion) useFsiAuxLib
]

/// <summary>picks a TFM for F# scripts based on the provided SDK version.</summary>
let tfmForRuntime =
    let netcore3 = Version(3, 0, 100, "")
    let netcore31 = Version(3, 1, 100, "")
    let netcore5 = Version(5, 0, 100, "")
    let netcore6 = Version(6, 0, 100, "")
    let netcore7 = Version(7, 0, 100, "")

    fun (sdkVersion: Version) ->

        if
            netcore7
            <= sdkVersion
        then
            "net7.0"
        else if
            netcore6
            <= sdkVersion
        then
            "net6.0"
        else if
            netcore5
            <= sdkVersion
        then
            "net5.0"
        else if
            netcore31
            <= sdkVersion
        then
            "netcoreapp3.1"
        else if
            netcore3
            <= sdkVersion
        then
            "netcoreapp3.0"
        else
            "netcoreapp2.2"
