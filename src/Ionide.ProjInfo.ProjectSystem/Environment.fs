namespace Ionide.ProjInfo.ProjectSystem

open System
open System.IO
open System.Runtime.InteropServices
open SemanticVersioning

[<RequireQualifiedAccess>]
module Environment =



    /// Determines if the current system is an Unix system.
    /// See http://www.mono-project.com/docs/faq/technical/#how-to-detect-the-execution-platform
    let isUnix = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)


    /// Determines if the current system is a MacOs system
    let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

    /// Determines if the current system is a Linux system
    let isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)

    /// Determines if the current system is a Windows system
    let isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

    let runningOnMono =
        try
            not << isNull <| Type.GetType "Mono.Runtime"
        with
        | _ -> false

    let private environVar v = Environment.GetEnvironmentVariable v

    let private programFilesX86 =
        let wow64 = environVar "PROCESSOR_ARCHITEW6432"
        let globalArch = environVar "PROCESSOR_ARCHITECTURE"

        match wow64, globalArch with
        | "AMD64", "AMD64"
        | null, "AMD64"
        | "x86", "AMD64" -> environVar "ProgramFiles(x86)"
        | _ -> environVar "ProgramFiles"
        |> fun detected ->
            if detected = null then
                @"C:\Program Files (x86)\"
            else
                detected

    // Below code slightly modified from FAKE MSBuildHelper.fs

    let private vsSkus =
        [ "Community"
          "Professional"
          "Enterprise"
          "BuildTools" ]

    let private vsVersions = [ "2019"; "2017" ]

    let private cartesian a b =
        [ for a' in a do
              for b' in b do
                  yield a', b' ]

    let private vsRoots =
        cartesian vsVersions vsSkus
        |> List.map (fun (version, sku) -> programFilesX86 </> "Microsoft Visual Studio" </> version </> sku)

    /// these are the single-instance installation paths on windows from FSharp versions < 4.5
    let private legacyFSharpInstallationPaths =
        [ "10.1"; "4.1"; "4.0"; "3.1"; "3.0" ]
        |> List.map (fun v -> programFilesX86 </> @"\Microsoft SDKs\F#\" </> v </> @"\Framework\v4.0")

    /// starting with F# 4.5 the binaries are installed in a side-by-side manner to a per-VS-edition folder
    let private sideBySideFSharpInstallationPaths =
        let pattern root =
            root </> "Common7" </> "IDE" </> "CommonExtensions" </> "Microsoft" </> "FSharp"

        vsRoots |> List.map pattern

    let private fsharpInstallationPath =
        sideBySideFSharpInstallationPaths @ legacyFSharpInstallationPaths |> List.tryFind Directory.Exists

    let fsi =
        // on netcore on non-windows we just deflect to fsharpi as usual
        if runningOnMono || not isWindows then
            Some "fsharpi"
        else
            // if running on windows, non-mono we can't yet send paths to the netcore version of fsi.exe so use the one from full-framework
            fsharpInstallationPath |> Option.map (fun root -> root </> "fsi.exe")

    let fsc =
        if runningOnMono || not isWindows then
            Some "fsharpc"
        else
            // if running on windows, non-mono we can't yet send paths to the netcore version of fsc.exe so use the one from full-framework
            fsharpInstallationPath |> Option.map (fun root -> root </> "fsc.exe")

    let fsharpCore =
        let dir =
            Path.GetDirectoryName(
                System
                    .Reflection
                    .Assembly
                    .GetExecutingAssembly()
                    .Location
            )

        dir </> "FSharp.Core.dll"

    let workspaceLoadDelay () =
        match System.Environment.GetEnvironmentVariable("FSAC_WORKSPACELOAD_DELAY") with
        | delayMs when not (String.IsNullOrWhiteSpace(delayMs)) ->
            match System.Int32.TryParse(delayMs) with
            | true, x -> TimeSpan.FromMilliseconds(float x)
            | false, _ -> TimeSpan.Zero
        | _ -> TimeSpan.Zero

    /// The sdk root that we assume for FSI-ref-location purposes.
    let dotnetSDKRoot =
        lazy
            (let fromEnv =
                Environment.GetEnvironmentVariable "DOTNET_ROOT"
                |> Option.ofObj
                |> Option.bind (fun d ->
                    let di = DirectoryInfo d

                    if di.Exists then
                        Some di
                    else
                        None)

             defaultArg fromEnv FSIRefs.defaultDotNetSDKRoot)

    /// <summary>
    /// Gets the highest-version of a set of versions based on an optional upper an lower bound
    /// </summary>
    /// <param name="range">optional bounds</param>
    /// <param name="versions">the set of versions to compare</param>
    /// <param name="includePrereleases">if true, prerelease versions will be considered</param>
    /// <returns>the max value found in that range, if any</returns>
    let maxVersionWithThreshold (range: Range option) (includePrereleases: bool) (versions: Version []) =
        let filterer =
            match range with
            | Some r -> (fun v -> r.IsSatisfied(v, includePrerelease = includePrereleases))
            | None -> fun _ -> true

        versions |> Array.filter filterer |> Array.sort |> Array.tryLast
