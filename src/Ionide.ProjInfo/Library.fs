namespace Ionide.ProjInfo

open System
open System.Collections.Generic
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework
open System.Runtime.Loader
open System.IO
open Microsoft.Build.Execution
open Types
open Microsoft.Build.Graph
open System.Diagnostics
open System.Runtime.InteropServices
open Ionide.ProjInfo.Logging
open Patterns

/// functions for .net sdk probing
module SdkDiscovery =

    let internal msbuildForSdk (sdkPath: DirectoryInfo) =
        Path.Combine(sdkPath.FullName, "MSBuild.dll")

    type DotnetRuntimeInfo = {
        RuntimeName: string
        Version: SemanticVersioning.Version
        Path: DirectoryInfo
    }

    let internal execDotnet (failOnError: bool) (cwd: DirectoryInfo) (binaryFullPath: FileInfo) args =
        let info = ProcessStartInfo()
        info.WorkingDirectory <- cwd.FullName
        info.FileName <- binaryFullPath.FullName

        for arg in args do
            info.ArgumentList.Add arg

        info.RedirectStandardOutput <- true
        use p = System.Diagnostics.Process.Start(info)

        let output =
            seq {
                while not p.StandardOutput.EndOfStream do
                    yield p.StandardOutput.ReadLine()
            }
            |> Seq.toArray

        p.WaitForExit()

        if p.ExitCode = 0 then
            output
        elif failOnError then
            let output =
                output
                |> String.concat "\n"

            failwith $"`{binaryFullPath.FullName} {args}` failed with exit code %i{p.ExitCode}. Output: %s{output}"
        else
            // for the legacy VS flow, whose behaviour is harder to test, we maintain compatibility with how proj-info
            // used to work before the above error handling was added
            output

    let private (|SemVer|_|) version =
        match SemanticVersioning.Version.TryParse version with
        | true, v -> Some v
        | false, _ -> None

    let private (|SdkOutputDirectory|) (path: string) =
        path.TrimStart('[').TrimEnd(']')
        |> DirectoryInfo

    let private (|RuntimeParts|_|) (line: string) =
        match line.IndexOf ' ' with
        | -1 -> None
        | n ->
            let runtimeName, rest = line.[0 .. n - 1], line.[n + 1 ..]

            match rest.IndexOf ' ' with
            | -1 -> None
            | n -> Some(runtimeName, rest.[0 .. n - 1], rest.[n + 1 ..])

    let private (|SdkParts|_|) (line: string) =
        match line.IndexOf ' ' with
        | -1 -> None
        | n -> Some(line.[0 .. n - 1], line.[n + 1 ..])

    /// Given the DOTNET_ROOT, that is the directory where the `dotnet` binary is present and the sdk/runtimes/etc are,
    /// enumerates the available runtimes in descending version order
    let runtimes (dotnetBinaryPath: FileInfo) : DotnetRuntimeInfo[] =
        execDotnet true dotnetBinaryPath.Directory dotnetBinaryPath [ "--list-runtimes" ]
        |> Seq.choose (fun line ->
            match line with
            | RuntimeParts(runtimeName, SemVer version, SdkOutputDirectory path) ->
                Some {
                    RuntimeName = runtimeName
                    Version = version
                    Path =
                        Path.Combine(path.FullName, string version)
                        |> DirectoryInfo
                }
            | line -> None
        )
        |> Seq.toArray

    type DotnetSdkInfo = {
        Version: SemanticVersioning.Version
        Path: DirectoryInfo
    }

    /// Given the DOTNET_ROOT, that is the directory where the `dotnet` binary is present and the sdk/runtimes/etc are,
    /// enumerates the available SDKs in descending version order
    let sdks (dotnetBinaryPath: FileInfo) : DotnetSdkInfo[] =
        execDotnet true dotnetBinaryPath.Directory dotnetBinaryPath [ "--list-sdks" ]
        |> Seq.choose (fun line ->
            match line with
            | SdkParts(SemVer sdkVersion, SdkOutputDirectory path) ->
                Some {
                    Version = sdkVersion
                    Path =
                        Path.Combine(path.FullName, string sdkVersion)
                        |> DirectoryInfo
                }
            | line -> None
        )
        |> Seq.toArray

    /// performs a `dotnet --version` command at the given directory to get the version of the
    /// SDK active at that location.
    let versionAt (cwd: DirectoryInfo) (dotnetBinaryPath: FileInfo) =
        execDotnet true cwd dotnetBinaryPath [ "--version" ]
        |> Seq.head
        |> function
            | version ->
                match SemanticVersioning.Version.TryParse version with
                | true, v -> Ok v
                | false, _ -> Error(dotnetBinaryPath, [ "--version" ], cwd, version)

// functions for legacy style project files
module LegacyFrameworkDiscovery =

    let isLinux = RuntimeInformation.IsOSPlatform OSPlatform.Linux
    let isMac = RuntimeInformation.IsOSPlatform OSPlatform.OSX

    let isUnix =
        isLinux
        || isMac

    let internal msbuildBinary =
        lazy
            (if isLinux then
                 "/usr/bin/msbuild"
                 |> FileInfo
                 |> Some
             elif isMac then
                 "/Library/Frameworks/Mono.framework/Versions/Current/Commands/msbuild"
                 |> FileInfo
                 |> Some
             else
                 // taken from https://github.com/microsoft/vswhere
                 // vswhere.exe is guaranteed to be at the following location. refer to https://github.com/Microsoft/vswhere/issues/162
                 let vsWhereDir =
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer")
                     |> DirectoryInfo

                 let vsWhereExe =
                     Path.Combine(vsWhereDir.FullName, "vswhere.exe")
                     |> FileInfo
                 // example: C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe

                 if not vsWhereExe.Exists then
                     failwith $"\"{vsWhereExe}\" does not exist. It is a expected to be present in '<ProgramFilesX86>/Microsoft Visual Studio/Installer' when resolving the MsBuild for legacy projects."

                 let msbuildExe =
                     SdkDiscovery.execDotnet false vsWhereDir vsWhereExe [
                         "-find"
                         "MSBuild\**\Bin\MSBuild.exe"
                     ]
                     |> Seq.tryHead
                     |> Option.map FileInfo

                 match msbuildExe with
                 | Some exe when exe.Exists -> Some exe
                 | _ -> None)

    let internal msbuildLibPath (msbuildDir: DirectoryInfo) =
        if isLinux then
            "/usr/lib/mono/xbuild"
        elif isMac then
            "/Library/Frameworks/Mono.framework/Versions/Current/lib/mono/xbuild"
        else
            // example: C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild
            Path.Combine(msbuildDir.FullName, "..", "..")
            |> Path.GetFullPath

[<RequireQualifiedAccess>]
module Init =

    let private ensureTrailer (path: string) =
        if
            path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
        then
            path
        else
            path
            + string Path.DirectorySeparatorChar

    let mutable private resolveHandler: Func<AssemblyLoadContext, System.Reflection.AssemblyName, System.Reflection.Assembly> =
        null

    let private resolveFromSdkRoot (sdkRoot: DirectoryInfo) : Func<AssemblyLoadContext, System.Reflection.AssemblyName, System.Reflection.Assembly> =
        Func<AssemblyLoadContext, System.Reflection.AssemblyName, System.Reflection.Assembly>(fun assemblyLoadContext assemblyName ->
            let paths = [
                Path.Combine(
                    sdkRoot.FullName,
                    assemblyName.Name
                    + ".dll"
                )
                Path.Combine(
                    sdkRoot.FullName,
                    "en",
                    assemblyName.Name
                    + ".dll"
                )
            ]

            match
                paths
                |> List.tryFind File.Exists
            with
            | Some path -> assemblyLoadContext.LoadFromAssemblyPath path
            | None -> null
        )


    /// <summary>
    /// Given the versioned path to an SDK root, sets up a few required environment variables
    /// that make the SDK and MSBuild behave the same as they do when invoked from the dotnet cli directly.
    /// </summary>
    /// <remarks>
    /// Specifically, this sets the following environment variables:
    /// <list type="bullet">
    /// <item><term>MSBUILD_EXE_PATH</term><description>the path to MSBuild.dll in the SDK</description></item>
    /// <item><term>MSBuildExtensionsPath</term><description>the slash-terminated root path for this SDK version</description></item>
    /// <item><term>MSBuildSDKsPath</term><description>the path to the Sdks folder inside this SDK version</description></item>
    /// <item><term>DOTNET_HOST_PATH</term><description>the path to the 'dotnet' binary</description></item>
    /// </list>
    ///
    /// It also hooks up assembly resolution to execute from the sdk's base path, per the suggestions in the MSBuildLocator project.
    ///
    /// See <see href="https://github.com/microsoft/MSBuildLocator/blob/d83904bff187ce8245f430b93e8b5fbfefb6beef/src/MSBuildLocator/MSBuildLocator.cs#L289">MSBuildLocator</see>,
    /// the <see href="https://github.com/dotnet/sdk/blob/a30e465a2e2ea4e2550f319a2dc088daaafe5649/src/Cli/dotnet/CommandFactory/CommandResolution/MSBuildProject.cs#L120">dotnet cli</see>, and
    /// the <see href="https://github.com/dotnet/sdk/blob/2c011f2aa7a91a386430233d5797452ca0821ed3/src/Cli/Microsoft.DotNet.Cli.Utils/MSBuildForwardingAppWithoutLogging.cs#L42-L44">dotnet msbuild command</see>
    /// for more examples of this.
    /// </remarks>
    /// <param name="sdkRoot">the versioned root path of a given SDK version, for example '/usr/local/share/dotnet/sdk/5.0.300'</param>
    /// <param name="dotnetExe">The full path to a dotnet binary to use as the root binary. This will be set as the DOTNET_HOST_PATH</param>
    /// <returns></returns>
    let setupForSdkVersion (sdkRoot: DirectoryInfo) (dotnetExe: FileInfo) =
        let msbuild = SdkDiscovery.msbuildForSdk sdkRoot

        // gotta set some env variables so msbuild interop works, see the locator for details: https://github.com/microsoft/MSBuildLocator/blob/d83904bff187ce8245f430b93e8b5fbfefb6beef/src/MSBuildLocator/MSBuildLocator.cs#L289
        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", msbuild)
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", ensureTrailer sdkRoot.FullName)
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(sdkRoot.FullName, "Sdks"))

        match System.Environment.GetEnvironmentVariable "DOTNET_HOST_PATH" with
        | null
        | "" -> Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", dotnetExe.FullName)
        | alreadySet -> ()

        if
            resolveHandler
            <> null
        then
            AssemblyLoadContext.Default.remove_Resolving resolveHandler

        resolveHandler <- resolveFromSdkRoot sdkRoot
        AssemblyLoadContext.Default.add_Resolving resolveHandler

    let internal setupForLegacyFramework (msbuildPathDir: DirectoryInfo) =
        let msbuildLibPath = LegacyFrameworkDiscovery.msbuildLibPath msbuildPathDir

        // gotta set some env variables so msbuild interop works
        if LegacyFrameworkDiscovery.isUnix then
            Environment.SetEnvironmentVariable("MSBuildBinPath", "/usr/lib/mono/msbuild/Current/bin")
            Environment.SetEnvironmentVariable("FrameworkPathOverride", "/usr/lib/mono/4.5")
        else
            // VsInstallRoot is required for legacy project files
            // example: C:\Program Files (x86)\Microsoft Visual Studio\2019\Community
            let vsInstallRoot =
                Path.Combine(msbuildPathDir.FullName, "..", "..", "..")
                |> Path.GetFullPath

            Environment.SetEnvironmentVariable("VsInstallRoot", vsInstallRoot)

        Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", ensureTrailer msbuildLibPath)

    /// Initialize the MsBuild integration. Returns path to MsBuild tool that was detected by Locator. Needs to be called before doing anything else.
    /// Call it again when the working directory changes.
    let init (workingDirectory: DirectoryInfo) (dotnetExe: FileInfo option) =
        let exe =
            dotnetExe
            |> Option.orElseWith (fun _ -> Paths.dotnetRoot.Value)

        match exe with
        | None -> failwith "No dotnet binary could be found via the DOTNET_HOST_PATH or DOTNET_ROOT environment variables, the PATH environment variable, or the default install locations"
        | Some exe ->

            match SdkDiscovery.versionAt workingDirectory exe with
            | Ok dotnetSdkVersionAtPath ->
                let sdks = SdkDiscovery.sdks exe

                let sdkInfo: SdkDiscovery.DotnetSdkInfo option =
                    sdks
                    |> Array.skipWhile (fun { Version = v } -> v < dotnetSdkVersionAtPath)
                    |> Array.tryHead

                match sdkInfo with
                | Some sdkInfo ->
                    let msbuild = SdkDiscovery.msbuildForSdk sdkInfo.Path
                    setupForSdkVersion sdkInfo.Path exe
                    ToolsPath msbuild
                | None ->
                    failwithf
                        $"Unable to get sdk versions at least from the string '{dotnetSdkVersionAtPath}'. This found sdks were {sdks
                                                                                                                                |> Array.toList}"
            | Error(dotnetExe, args, cwd, erroringVersionString) -> failwithf $"Unable to parse sdk version from the string '{erroringVersionString}'. This value came from running `{dotnetExe} {args}` at path {cwd}"

[<RequireQualifiedAccess>]
type BinaryLogGeneration =
    /// No binary logs will be generated for this build
    | Off
    /// Binary logs will be generated and placed in the directory specified. They will have names of the form `{directory}/{project_name}.binlog`
    | Within of directory: DirectoryInfo


/// <summary>
/// Low level APIs for single project loading. Doesn't provide caching, and doesn't follow p2p references.
/// In most cases you want to use an <see cref="Ionide.ProjInfo.IWorkspaceLoader"/> type instead
/// </summary>
module ProjectLoader =

    type LoadedProject =
        internal
        | StandardProject of ProjectInstance
        | TraversalProject of ProjectInstance
        /// This could be things like shproj files, or other things that aren't standard projects
        | Other of ProjectInstance

    let (|IsTraversal|_|) (p: ProjectInstance) =
        match p.GetProperty "IsTraversal" with
        | null -> None
        | p when Boolean.TryParse(p.EvaluatedValue) = (true, false) -> None
        | _ -> Some()

    let (|DesignTimeCapable|_|) (targets: string seq) (p: ProjectInstance) =
        if Seq.forall p.Targets.ContainsKey targets then
            Some()
        else
            None

    let internal projectLoaderLogger = lazy (LogProvider.getLoggerByName "ProjectLoader")

    let msBuildToLogProvider () =
        let msBuildLogger = LogProvider.getLoggerByName "MsBuild"

        { new ILogger with
            member this.Initialize(eventSource: IEventSource) : unit =
                eventSource.ErrorRaised.Add(fun t -> msBuildLogger.error (Log.setMessage t.Message))
                eventSource.WarningRaised.Add(fun t -> msBuildLogger.warn (Log.setMessage t.Message))
                eventSource.BuildStarted.Add(fun t -> msBuildLogger.info (Log.setMessage t.Message))
                eventSource.BuildFinished.Add(fun t -> msBuildLogger.info (Log.setMessage t.Message))

                eventSource.MessageRaised.Add(fun t ->
                    match t.Importance with
                    | MessageImportance.High -> msBuildLogger.debug (Log.setMessage t.Message)
                    | MessageImportance.Normal
                    | MessageImportance.Low
                    | _ -> msBuildLogger.trace (Log.setMessage t.Message)
                )

                eventSource.TargetStarted.Add(fun t -> msBuildLogger.trace (Log.setMessage t.Message))
                eventSource.TargetFinished.Add(fun t -> msBuildLogger.trace (Log.setMessage t.Message))
                eventSource.TaskStarted.Add(fun t -> msBuildLogger.trace (Log.setMessage t.Message))
                eventSource.TaskFinished.Add(fun t -> msBuildLogger.trace (Log.setMessage t.Message))

            member this.Parameters
                with get (): string = ""
                and set (v: string): unit = ()

            member this.Shutdown() : unit = ()

            member this.Verbosity
                with get (): LoggerVerbosity = LoggerVerbosity.Detailed
                and set (v: LoggerVerbosity): unit = ()
        }

    let internal stringWriterLogger (writer: StringWriter) =
        { new ILogger with
            member this.Initialize(eventSource: IEventSource) : unit =
                // eventSource.ErrorRaised.Add(fun t -> writer.WriteLine t.Message) //Only log errors
                eventSource.AnyEventRaised.Add(fun t -> writer.WriteLine t.Message)

            member this.Parameters: string = ""

            member this.Parameters
                with set (v: string): unit = printfn "v"

            member this.Shutdown() : unit = ()
            member this.Verbosity: LoggerVerbosity = LoggerVerbosity.Detailed

            member this.Verbosity
                with set (v: LoggerVerbosity): unit = ()
        }

    let mergeGlobalProperties (collection: ProjectCollection) (otherProperties: IDictionary<string, string>) =
        let combined = Dictionary(collection.GlobalProperties)

        for kvp in otherProperties do
            combined.Add(kvp.Key, kvp.Value)

        combined

    type ProjectAlreadyLoaded(projectPath: string, collection: ProjectCollection, properties: IDictionary<string, string>, innerException) =
        inherit System.Exception("", innerException)
        let mutable _message = null

        override this.Message =
            match _message with
            | null ->
                // if the project is already loaded throw a nicer message
                let message = Text.StringBuilder()
                let appendLine (t: string) (sb: Text.StringBuilder) = sb.AppendLine t

                message
                |> appendLine $"The project '{projectPath}' already exists in the project collection with the same global properties."
                |> appendLine "The global properties requested were:"
                |> ignore

                for (KeyValue(k, v)) in properties do
                    message.AppendLine($"  {k} = {v}")
                    |> ignore

                message.AppendLine()
                |> ignore

                message.AppendLine("There are projects of the following properties already in the collection:")
                |> ignore

                for project in collection.GetLoadedProjects(projectPath) do
                    message.AppendLine($"Evaluation #{project.LastEvaluationId}")
                    |> ignore

                    for (KeyValue(k, v)) in project.GlobalProperties do
                        message.AppendLine($"  {k} = {v}")
                        |> ignore

                    message.AppendLine()
                    |> ignore

                _message <- message.ToString()
            | _ -> ()

            _message

    // it's _super_ important that the 'same' project (path + properties) is only created once in a project collection, so we have to check on this here
    let findOrCreateMatchingProject path (collection: ProjectCollection) globalProps =
        let createNewProject properties =
            try
                Project(
                    projectFile = path,
                    projectCollection = collection,
                    globalProperties = properties,
                    toolsVersion = null,
                    loadSettings =
                        (ProjectLoadSettings.IgnoreMissingImports
                         ||| ProjectLoadSettings.IgnoreInvalidImports)
                )
            with :? System.InvalidOperationException as ex ->
                raise (ProjectAlreadyLoaded(path, collection, properties, ex))

        let hasSameGlobalProperties (globalProps: IDictionary<string, string>) (incomingProject: Project) =
            if
                incomingProject.GlobalProperties.Count
                <> globalProps.Count
            then
                false
            else
                globalProps
                |> Seq.forall (fun (KeyValue(k, v)) ->
                    incomingProject.GlobalProperties.ContainsKey k
                    && incomingProject.GlobalProperties.[k] = v
                )

        lock
            (collection)
            (fun _ ->
                match collection.GetLoadedProjects(path) with
                | null -> createNewProject globalProps
                | existingProjects when existingProjects.Count = 0 -> createNewProject globalProps
                | existingProjects ->
                    let totalGlobalProps = mergeGlobalProperties collection globalProps

                    existingProjects
                    |> Seq.tryFind (hasSameGlobalProperties totalGlobalProps)
                    |> Option.defaultWith (fun _ -> createNewProject globalProps)
            )

    let getTfm (pi: ProjectInstance) isLegacyFrameworkProj =
        let tfm =
            pi.GetPropertyValue(
                if isLegacyFrameworkProj then
                    "TargetFrameworkVersion"
                else
                    "TargetFramework"
            )

        if String.IsNullOrWhiteSpace tfm then
            let targetFrameworks = pi.GetPropertyValue "TargetFrameworks"

            match targetFrameworks with
            | null -> None
            | tfms ->
                match tfms.Split(';') with
                | [||] -> None
                | tfms -> Some tfms.[0]
        else
            Some tfm

    let loadProjectAndGetTFM (path: string) projectCollection readingProps isLegacyFrameworkProj =
        let project = findOrCreateMatchingProject path projectCollection readingProps
        let pi = project.CreateProjectInstance()
        getTfm pi isLegacyFrameworkProj

    let createLoggers (path: string) (binaryLogs: BinaryLogGeneration) (sw: StringWriter) =
        let swLogger = stringWriterLogger (sw)
        let msBuildLogger = msBuildToLogProvider ()

        let logFilePath (dir: DirectoryInfo, projectPath: string) =
            let projectFileName = Path.GetFileName projectPath
            let logFileName = Path.ChangeExtension(projectFileName, ".binlog")
            Path.Combine(dir.FullName, logFileName)

        [
            swLogger
            msBuildLogger
            match binaryLogs with
            | BinaryLogGeneration.Off -> ()
            | BinaryLogGeneration.Within dir -> Microsoft.Build.Logging.BinaryLogger(Parameters = logFilePath (dir, path)) :> ILogger

        ]

    let getGlobalProps (tfm: string option) (globalProperties: (string * string) list) (propsSetFromParentCollection: Set<string>) =
        [
            "ProvideCommandLineArgs", "true"
            "DesignTimeBuild", "true"
            "SkipCompilerExecution", "true"
            "GeneratePackageOnBuild", "false"
            "Configuration", "Debug"
            "DefineExplicitDefaults", "true"
            "BuildProjectReferences", "false"
            "UseCommonOutputDirectory", "false"
            "NonExistentFile", Path.Combine("__NonExistentSubDir__", "__NonExistentFile__") // Required by the Clean Target
            if tfm.IsSome then
                "TargetFramework", tfm.Value
            "DotnetProjInfo", "true"
            yield! globalProperties
        ]
        |> List.filter (fun (ourProp, _) -> not (propsSetFromParentCollection.Contains ourProp))
        |> dict


    ///<summary>
    /// These are a list of build targets that are run during a design-time build (mostly).
    /// The list comes partially from <see href="https://github.com/dotnet/project-system/blob/main/docs/design-time-builds.md#targets-that-run-during-design-time-builds">the msbuild docs</see>
    /// and partly from experience.
    ///
    /// <remarks>
    /// <list type="bullet">
    /// <item><term>ResolveAssemblyReferencesDesignTime</term><description>resolves Reference items</description></item>
    /// <item><term>ResolveProjectReferencesDesignTime</term><description>resolve ProjectReference items</description></item>
    /// <item><term>ResolvePackageDependenciesDesignTime</term><description>resolve PackageReference items</description></item>
    /// <item><term>_GenerateCompileDependencyCache</term><description>defined in the F# targets to populate any required dependencies</description></item>
    /// <item><term>_ComputeNonExistentFileProperty</term><description>when built forces a re-compile (which we want to ensure we get fresh results each time we crack a project)</description></item>
    /// <item><term>CoreCompile</term><description>actually generates the FSC command line arguments</description></item>
    /// </list>
    /// </remarks>
    /// </summary>
    let designTimeBuildTargets isLegacyFrameworkProjFile =
        if isLegacyFrameworkProjFile then
            [|
                "_GenerateCompileDependencyCache"
                "_ComputeNonExistentFileProperty"
                "CoreCompile"
            |]
        else
            [|
                "ResolveAssemblyReferencesDesignTime"
                "ResolveProjectReferencesDesignTime"
                "ResolvePackageDependenciesDesignTime"
                // Populates ReferencePathWithRefAssemblies which CoreCompile requires.
                // This can be removed one day when Microsoft.FSharp.Targets calls this.
                "FindReferenceAssembliesForReferences"
                "_GenerateCompileDependencyCache"
                "_ComputeNonExistentFileProperty"
                "BeforeBuild"
                "BeforeCompile"
                "CoreCompile"
            |]

    let setLegacyMsbuildProperties isOldStyleProjFile =
        match LegacyFrameworkDiscovery.msbuildBinary.Value with
        | Some file ->
            let msbuildBinaryDir = file.Directory
            Init.setupForLegacyFramework msbuildBinaryDir
        | _ -> ()

    let loadProject (path: string) (binaryLogs: BinaryLogGeneration) (projectCollection: ProjectCollection) =
        try
            let isLegacyFrameworkProjFile =
                if
                    path.ToLower().EndsWith ".fsproj"
                    && File.Exists path
                then
                    let legacyProjFormatXmlns = "xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\""
                    let lines: seq<string> = File.ReadLines path

                    (Seq.tryFind (fun (line: string) -> line.Contains legacyProjFormatXmlns) lines)
                    |> Option.isSome
                else
                    false

            let collectionProps =
                projectCollection.GlobalProperties.Keys
                |> Set.ofSeq

            let readingProps = getGlobalProps None [] collectionProps

            if isLegacyFrameworkProjFile then
                setLegacyMsbuildProperties isLegacyFrameworkProjFile

            let tfm = loadProjectAndGetTFM path projectCollection readingProps isLegacyFrameworkProjFile

            let globalProperties = getGlobalProps tfm [] collectionProps
            let project = findOrCreateMatchingProject path projectCollection globalProperties
            use sw = new StringWriter()

            let loggers = createLoggers path binaryLogs sw

            let pi = project.CreateProjectInstance()
            let designTimeTargets = designTimeBuildTargets isLegacyFrameworkProjFile

            let doDesignTimeBuild () =
                let build = pi.Build(designTimeTargets, loggers)

                if build then
                    Ok(StandardProject pi)
                else
                    Error(sw.ToString())

            match pi with
            | IsTraversal -> Ok(TraversalProject pi)
            | DesignTimeCapable designTimeTargets -> doDesignTimeBuild ()
            | _ -> Ok(Other pi)

        with exc ->
            projectLoaderLogger.Value.error (
                Log.setMessage "Generic error while loading project {path}"
                >> Log.addExn exc
                >> Log.addContextDestructured "path" path
            )

            Error(exc.Message)

    let getFscArgs (p: ProjectInstance) =
        p.Items
        |> Seq.filter (fun p -> p.ItemType = "FscCommandLineArgs")
        |> Seq.map (fun p -> p.EvaluatedInclude)

    let getCscArgs (p: ProjectInstance) =
        p.Items
        |> Seq.filter (fun p -> p.ItemType = "CscCommandLineArgs")
        |> Seq.map (fun p -> p.EvaluatedInclude)

    let getP2PRefs (project) =
        match project with
        | TraversalProject p ->
            let references =
                p.Items
                |> Seq.filter (fun p -> p.ItemType = "ProjectReference")
                |> Seq.toList

            let mappedItems = ResizeArray()

            for item in references do
                let relativePath = item.EvaluatedInclude
                let fullPath = Path.GetFullPath(relativePath, item.Project.Directory)

                {
                    RelativePath = relativePath
                    ProjectFileName = fullPath
                    TargetFramework = ""
                }
                |> mappedItems.Add

            Seq.toList mappedItems

        | StandardProject p ->
            p.Items
            |> Seq.choose (fun p ->
                if
                    p.ItemType
                    <> "_MSBuildProjectReferenceExistent"
                then
                    None
                else
                    let relativePath = p.EvaluatedInclude
                    let path = p.GetMetadataValue "FullPath"

                    let tfms =
                        if p.HasMetadata "TargetFramework" then
                            p.GetMetadataValue "TargetFramework"
                        else
                            p.GetMetadataValue "TargetFrameworks"

                    Some {
                        RelativePath = relativePath
                        ProjectFileName = path
                        TargetFramework = tfms
                    }
            )
            |> Seq.toList
        | Other(_) -> List.empty

    let getCompileItems (p: ProjectInstance) =
        p.Items
        |> Seq.filter (fun p -> p.ItemType = "Compile")
        |> Seq.map (fun p ->
            let name = p.EvaluatedInclude

            let link =
                if p.HasMetadata "Link" then
                    Some(p.GetMetadataValue "Link")
                else
                    None

            let fullPath = p.GetMetadataValue "FullPath"

            {
                Name = name
                FullPath = fullPath
                Link = link
                Metadata =
                    p.Metadata
                    |> Seq.map (fun pm -> pm.Name, pm.EvaluatedValue)
                    |> Map.ofSeq
            }
        )

    let getNuGetReferences (p: ProjectInstance) =
        p.Items
        |> Seq.filter (fun p ->
            p.ItemType = "Reference"
            && p.GetMetadataValue "NuGetSourceType" = "Package"
        )
        |> Seq.map (fun p ->
            let name = p.GetMetadataValue "NuGetPackageId"
            let version = p.GetMetadataValue "NuGetPackageVersion"
            let fullPath = p.GetMetadataValue "FullPath"

            {
                Name = name
                Version = version
                FullPath = fullPath
            }
        )

    let getProperties (p: ProjectInstance) (properties: string list) =
        p.Properties
        |> Seq.filter (fun p -> List.contains p.Name properties)
        |> Seq.map (fun p -> {
            Name = p.Name
            Value = p.EvaluatedValue
        })

    let (|ConditionEquals|_|) (str: string) (arg: string) =
        if System.String.Compare(str, arg, System.StringComparison.OrdinalIgnoreCase) = 0 then
            Some()
        else
            None

    let (|StringList|_|) (str: string) =
        str.Split([| ';' |], System.StringSplitOptions.RemoveEmptyEntries)
        |> List.ofArray
        |> Some

    let getSdkInfo (props: Property seq) =

        let msbuildPropBool (s: Property) =
            match s.Value.Trim() with
            | "" -> None
            | ConditionEquals "True" -> Some true
            | _ -> Some false

        let msbuildPropStringList (s: Property) =
            match s.Value.Trim() with
            | "" -> []
            | StringList list -> list
            | _ -> []

        let msbuildPropBool (prop) =
            props
            |> Seq.tryFind (fun n -> n.Name = prop)
            |> Option.bind msbuildPropBool

        let msbuildPropStringList prop =
            props
            |> Seq.tryFind (fun n -> n.Name = prop)
            |> Option.map msbuildPropStringList

        let msbuildPropString prop =
            props
            |> Seq.tryFind (fun n -> n.Name = prop)
            |> Option.map (fun n -> n.Value.Trim())

        {
            IsTestProject =
                msbuildPropBool "IsTestProject"
                |> Option.defaultValue false
            Configuration =
                msbuildPropString "Configuration"
                |> Option.defaultValue ""
            IsPackable =
                msbuildPropBool "IsPackable"
                |> Option.defaultValue false
            TargetFramework =
                msbuildPropString "TargetFramework"
                |> Option.defaultValue ""
            TargetFrameworkIdentifier =
                msbuildPropString "TargetFrameworkIdentifier"
                |> Option.defaultValue ""
            TargetFrameworkVersion =
                msbuildPropString "TargetFrameworkVersion"
                |> Option.defaultValue ""

            MSBuildAllProjects =
                msbuildPropStringList "MSBuildAllProjects"
                |> Option.defaultValue []
            MSBuildToolsVersion =
                msbuildPropString "MSBuildToolsVersion"
                |> Option.defaultValue ""

            ProjectAssetsFile =
                msbuildPropString "ProjectAssetsFile"
                |> Option.defaultValue ""
            RestoreSuccess =
                match msbuildPropString "TargetFrameworkVersion" with
                | Some _ -> true
                | None ->
                    msbuildPropBool "RestoreSuccess"
                    |> Option.defaultValue false

            Configurations =
                msbuildPropStringList "Configurations"
                |> Option.defaultValue []
            TargetFrameworks =
                msbuildPropStringList "TargetFrameworks"
                |> Option.defaultValue []

            RunArguments = msbuildPropString "RunArguments"
            RunCommand = msbuildPropString "RunCommand"

            IsPublishable = msbuildPropBool "IsPublishable"
        }

#if DEBUG
    let private printProperties (props: Map<string, Set<string>>) =
        let file = Path.Combine(__SOURCE_DIRECTORY__, "props.txt")

        props
        |> Map.iter (fun key value ->
            IO.File.AppendAllLines(file, [ $"Property: {key}" ])

            value
            |> Set.iter (fun v -> IO.File.AppendAllLines(file, [ $"  Value: {v}" ]))
        )

    let private printItems (items: Map<string, Set<string * Map<string, string>>>) =
        let file = Path.Combine(__SOURCE_DIRECTORY__, "items.txt")

        items
        |> Map.iter (fun key value ->
            IO.File.AppendAllLines(file, [ $"ItemType: {key}" ])

            value
            |> Set.iter (fun (include_, metadata) ->
                IO.File.AppendAllLines(file, [ $"  Value: {include_}" ])

                metadata
                |> Map.iter (fun mk mv -> IO.File.AppendAllLines(file, [ $"    {mk}: {mv}" ]))
            )
        )
#endif

    let mapToProject
        (path: string)
        (compilerArgs: string seq)
        (p2p: ProjectReference seq)
        (compile: CompileItem seq)
        (nugetRefs: PackageReference seq)
        (sdkInfo: ProjectSdkInfo)
        (props: Property seq)
        (customProps: Property seq)
        (analyzers: Analyzer list)
        (allProps: Map<string, Set<string>>)
        (allItems: Map<string, Set<string * Map<string, string>>>)
        =
        let projDir = Path.GetDirectoryName path

        let outputType, sourceFiles, otherOptions =
            if path.EndsWith ".fsproj" then
                let fscArgsNormalized =
                    //workaround, arguments in rsp can use relative paths
                    compilerArgs
                    |> Seq.map (FscArguments.useFullPaths projDir)
                    |> Seq.toList

                let sourceFiles, otherOptions =
                    fscArgsNormalized
                    |> List.partition (FscArguments.isSourceFile path)

                let outputType = FscArguments.outType fscArgsNormalized
                outputType, sourceFiles, otherOptions
            else
                let cscArgsNormalized =
                    //workaround, arguments in rsp can use relative paths
                    compilerArgs
                    |> Seq.map (CscArguments.useFullPaths projDir)
                    |> Seq.toList

                let sourceFiles, otherOptions =
                    cscArgsNormalized
                    |> List.partition (CscArguments.isSourceFile path)

                let outputType = CscArguments.outType cscArgsNormalized
                outputType, sourceFiles, otherOptions

        let compileItems =
            sourceFiles
            |> List.map (
                VisualTree.getCompileProjectItem
                    (compile
                     |> Seq.toList)
                    path
            )

        let project: ProjectOptions = {
            ProjectId = Some path
            ProjectFileName = path
            TargetFramework = sdkInfo.TargetFramework
            SourceFiles = sourceFiles
            OtherOptions = otherOptions
            ReferencedProjects = List.ofSeq p2p
            PackageReferences = List.ofSeq nugetRefs
            LoadTime = DateTime.Now
            TargetPath =
                props
                |> Seq.tryPick (fun n ->
                    if n.Name = "TargetPath" then
                        Some n.Value
                    else
                        None
                )
                |> Option.defaultValue ""
            TargetRefPath =
                props
                |> Seq.tryPick (fun n ->
                    if n.Name = "TargetRefPath" then
                        Some n.Value
                    else
                        None
                )
            ProjectOutputType = outputType
            ProjectSdkInfo = sdkInfo
            Items = compileItems
            Properties = List.ofSeq props
            CustomProperties = List.ofSeq customProps
            Analyzers = analyzers
            AllProperties = allProps
            AllItems = allItems
        }


        project

    [<RequireQualifiedAccess>]
    type LoadedProjectInfo =
        | StandardProjectInfo of ProjectOptions
        | TraversalProjectInfo of ProjectReference list
        | OtherProjectInfo of ProjectInstance

    let getLoadedProjectInfo (path: string) customProperties project : Result<LoadedProjectInfo, string> =
        // let (LoadedProject p) = project
        // let path = p.FullPath

        match project with
        | LoadedProject.TraversalProject t ->
            LoadedProjectInfo.TraversalProjectInfo(getP2PRefs project)
            |> Ok
        | LoadedProject.StandardProject p ->
            let properties = [
                "OutputType"
                "IsTestProject"
                "TargetPath"
                "Configuration"
                "IsPackable"
                "TargetFramework"
                "TargetFrameworkIdentifier"
                "TargetFrameworkVersion"
                "MSBuildAllProjects"
                "ProjectAssetsFile"
                "RestoreSuccess"
                "Configurations"
                "TargetFrameworks"
                "RunArguments"
                "RunCommand"
                "IsPublishable"
                "BaseIntermediateOutputPath"
                "IntermediateOutputPath"
                "TargetPath"
                "TargetRefPath"
                "IsCrossTargetingBuild"
                "TargetFrameworks"
            ]

            let p2pRefs = getP2PRefs project

            let commandLineArgs =
                if path.EndsWith ".fsproj" then
                    getFscArgs p
                else if path.EndsWith ".csproj" then
                    getCscArgs p
                else
                    Seq.empty

            let compileItems = getCompileItems p
            let nuGetRefs = getNuGetReferences p
            let props = getProperties p properties
            let sdkInfo = getSdkInfo props
            let customProps = getProperties p customProperties

            let allProperties =
                (Map.empty, p.Properties)
                ||> Seq.fold (fun acc p ->
                    Map.change
                        p.Name
                        (function
                        | None ->
                            Set.singleton p.EvaluatedValue
                            |> Some
                        | Some v ->
                            v
                            |> Set.add p.EvaluatedValue
                            |> Some)
                        acc
                )

            let allItems =
                (Map.empty, p.Items)
                ||> Seq.fold (fun acc item ->
                    let value = item.EvaluatedInclude

                    let metadata =
                        item.Metadata
                        |> Seq.map (fun m -> m.Name, m.EvaluatedValue)
                        |> Map.ofSeq

                    Map.change
                        item.ItemType
                        (function
                        | None ->
                            Set.singleton (value, metadata)
                            |> Some
                        | Some items ->
                            items
                            |> Set.add (value, metadata)
                            |> Some)
                        acc

                )


            let analyzers =
                let analyzerLikePath = Path.Combine("analyzers", "dotnet", "fs")

                allProperties
                |> Map.filter (fun k v ->
                    k.StartsWith("Pkg")
                    && v
                       |> Set.exists (fun s -> Path.Exists(Path.Combine(s, analyzerLikePath)))
                )
                |> Seq.collect (fun (KeyValue(k, v)) ->
                    v
                    |> Seq.map (fun v ->
                        let analyzerPath = Path.Combine(v, analyzerLikePath)

                        {
                            PropertyName = k
                            NugetPackageRoot = v
                            DllRootPath = analyzerPath
                        }

                    )
                )
                |> Seq.toList


            if not sdkInfo.RestoreSuccess then
                Error "not restored"
            else
                let proj =
                    mapToProject path commandLineArgs p2pRefs compileItems nuGetRefs sdkInfo props customProps analyzers allProperties allItems

                Ok(LoadedProjectInfo.StandardProjectInfo proj)
        | LoadedProject.Other p -> Ok(LoadedProjectInfo.OtherProjectInfo p)

/// A type that turns project files or solution files into deconstructed options.
/// Use this in conjunction with the other ProjInfo libraries to turn these options into
/// ones compatible for use with FCS directly.
type IWorkspaceLoader =

    /// <summary>
    /// Load a list of projects, extracting a set of custom build properties from the build results
    /// in addition to the properties used to power the ProjectOption creation.
    /// </summary>
    /// <param name="projectPaths">the projects to load</param>
    /// <param name="customProperties">any custom msbuild properties that should be extracted from the build results. these will be available under the CustomProperties property of the returned ProjectOptions</param>
    /// <param name="binaryLog">determines if and where to write msbuild binary logs</param>
    /// <returns>the loaded project structures</returns>
    abstract member LoadProjects: projectPaths: string list * customProperties: list<string> * binaryLog: BinaryLogGeneration -> seq<ProjectOptions>

    /// <summary>
    /// Load a list of projects with no additional custom properties, without generating binary logs
    /// </summary>
    /// <returns>the loaded project structures</returns>
    abstract member LoadProjects: projectPaths: string list -> seq<ProjectOptions>

    /// <summary>
    /// Load every project contained in the solution file, extra
    /// </summary>
    /// <returns>the loaded project structures</returns>
    /// <param name="solutionPath">path to the solution to be loaded</param>
    /// <param name="customProperties">any custom msbuild properties that should be extracted from the build results. these will be available under the CustomProperties property of the returned ProjectOptions</param>
    /// <param name="binaryLog">determines if and where to write msbuild binary logs</param>
    abstract member LoadSln: solutionPath: string * customProperties: list<string> * binaryLog: BinaryLogGeneration -> seq<ProjectOptions>

    /// <summary>
    /// Load every project contained in the solution file with no additional custom properties, without generating binary logs
    /// </summary>
    /// <param name="solutionPath">path to the solution to be loaded</param>
    /// <returns>the loaded project structures</returns>
    abstract member LoadSln: solutionPath: string -> seq<ProjectOptions>

    [<CLIEvent>]
    abstract Notifications: IEvent<WorkspaceProjectState>

module WorkspaceLoaderViaProjectGraph =
    let locker = obj ()


type WorkspaceLoaderViaProjectGraph private (toolsPath, ?globalProperties: (string * string) list) =
    let (ToolsPath toolsPath) = toolsPath
    let logger = LogProvider.getLoggerFor<WorkspaceLoaderViaProjectGraph> ()
    let loadingNotification = new Event<Types.WorkspaceProjectState>()

    let projectCollection () =
        new ProjectCollection(
            globalProperties = dict (defaultArg globalProperties []),
            loggers = null,
            remoteLoggers = null,
            toolsetDefinitionLocations = ToolsetDefinitionLocations.Local,
            loadProjectsReadOnly = true,
            maxNodeCount = Environment.ProcessorCount,
            onlyLogCriticalEvents = false
        )

    let handleProjectGraphFailures f =
        try
            f ()
            |> Some
        with
        | InvalidProjectException e ->
            let p = e.ProjectFile
            loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotFound(p)))
            None
        | ex ->
            logger.error (
                Log.setMessage "error while building projects via graph build"
                >> Log.addExn ex
            )

            None

    let projectInstanceFactory projectPath (globalProperties: IDictionary<string, string>) (projectCollection: ProjectCollection) =
        let loadedProject = ProjectLoader.findOrCreateMatchingProject projectPath projectCollection globalProperties

        let projInstance = loadedProject.CreateProjectInstance()
        let tfm = ProjectLoader.getTfm projInstance false

        let ourGlobalProperties =
            ProjectLoader.getGlobalProps
                tfm
                []
                (globalProperties.Keys
                 |> Set.ofSeq
                 |> Set.union (Set.ofSeq projectCollection.GlobalProperties.Keys))

        let tfm_specific_project = ProjectLoader.findOrCreateMatchingProject projectPath projectCollection ourGlobalProperties
        tfm_specific_project.CreateProjectInstance()

    let projectGraphProjects (paths: string seq) =

        handleProjectGraphFailures
        <| fun () ->
            use per_request_collection = projectCollection ()

            paths
            |> Seq.iter (fun p -> loadingNotification.Trigger(WorkspaceProjectState.Loading p))

            let designTimeTargets = ProjectLoader.designTimeBuildTargets false

            let graph =
                let entryPoints =
                    paths
                    |> Seq.map ProjectGraphEntryPoint
                    |> List.ofSeq

                let g: ProjectGraph =
                    ProjectGraph(entryPoints, projectCollection = per_request_collection, projectInstanceFactory = projectInstanceFactory)
                // When giving ProjectGraph a singular project, g.EntryPointNodes only contains that project.
                // To get it to build the Graph with all the dependencies we need to look at all the ProjectNodes
                // and tell the graph to use all as potentially an entrypoint
                // Additionally, we need to filter out any projects that are not design time capable
                let nodes =
                    g.ProjectNodes
                    |> Seq.choose (fun pn ->
                        match pn.ProjectInstance with
                        | ProjectLoader.DesignTimeCapable designTimeTargets ->

                            ProjectGraphEntryPoint pn.ProjectInstance.FullPath
                            |> Some
                        | _ -> None
                    )

                ProjectGraph(nodes, projectCollection = per_request_collection, projectInstanceFactory = projectInstanceFactory)

            graph

    let projectGraphSln (path: string) =
        handleProjectGraphFailures
        <| fun () ->
            let pg = ProjectGraph(path, ProjectCollection.GlobalProjectCollection, projectInstanceFactory)

            pg.ProjectNodes
            |> Seq.distinctBy (fun p -> p.ProjectInstance.FullPath)
            |> Seq.map (fun p -> p.ProjectInstance.FullPath)
            |> Seq.iter (fun p -> loadingNotification.Trigger(WorkspaceProjectState.Loading p))

            pg

    let loadProjects (projects: ProjectGraph, customProperties: string list, binaryLogs: BinaryLogGeneration) =
        let handleError (msbuildErrors: string) (e: exn) =
            let msg = e.Message

            logger.error (
                Log.setMessageI $"Failed loading {msbuildErrors:msbuildErrors}"
                >> Log.addExn e
            )

            projects.ProjectNodes
            |> Seq.distinctBy (fun p -> p.ProjectInstance.FullPath)
            |> Seq.iter (fun p ->

                let p = p.ProjectInstance.FullPath

                if msg.Contains "The project file could not be loaded." then
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotFound(p)))
                elif msg.Contains "not restored" then
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotRestored(p)))
                else
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, GenericError(p, msg)))
            )

            Seq.empty

        try
            lock WorkspaceLoaderViaProjectGraph.locker
            <| fun () ->
                let allKnown =
                    projects.ProjectNodes
                    |> Seq.distinctBy (fun p -> p.ProjectInstance.FullPath)

                let allKnownNames =
                    allKnown
                    |> Seq.map (fun p -> p.ProjectInstance.FullPath)
                    |> Seq.toList

                logger.info (
                    Log.setMessage "Started loading projects {count} {projects}"
                    >> Log.addContextDestructured
                        "count"
                        (allKnownNames
                         |> Seq.length)
                    >> Log.addContextDestructured "projects" (allKnownNames)
                )

                let gbr =
                    GraphBuildRequestData(
                        projectGraph = projects,
                        targetsToBuild = ProjectLoader.designTimeBuildTargets false,
                        hostServices = null,
                        flags =
                            (BuildRequestDataFlags.ReplaceExistingProjectInstance
                             ||| BuildRequestDataFlags.ClearCachesAfterBuild)
                    )

                let bm = BuildManager.DefaultBuildManager
                use sw = new StringWriter()
                let loggers = ProjectLoader.createLoggers "graph-build" binaryLogs sw
                let buildParameters = BuildParameters(Loggers = loggers)

                buildParameters.ProjectLoadSettings <-
                    ProjectLoadSettings.RecordEvaluatedItemElements
                    ||| ProjectLoadSettings.ProfileEvaluation
                    ||| ProjectLoadSettings.IgnoreMissingImports
                    ||| ProjectLoadSettings.IgnoreInvalidImports

                buildParameters.LogInitialPropertiesAndItems <- true
                bm.BeginBuild(buildParameters)

                let result = bm.BuildRequest gbr

                bm.EndBuild()

                let msbuildMessage = sw.ToString()

                if
                    result.Exception
                    |> isNull
                    |> not
                then
                    handleError msbuildMessage result.Exception
                else
                    let builtProjects =
                        result.ResultsByNode.Keys
                        |> Seq.collect (fun (pgn: ProjectGraphNode) -> seq { yield pgn.ProjectInstance })
                        |> Seq.toArray

                    let projectsBuiltCount = builtProjects.Length

                    match result.OverallResult with
                    | BuildResultCode.Success ->
                        logger.info (
                            Log.setMessageI $"Overall Build: {result.OverallResult:overallCode}, projects built {projectsBuiltCount:count}"
                            >> Log.addExn result.Exception
                        )
                    | BuildResultCode.Failure
                    | _ ->
                        logger.error (
                            Log.setMessageI $"Overall Build: {result.OverallResult:overallCode}, projects built {projectsBuiltCount:count} : {msbuildMessage:msbuildMessage} "
                            >> Log.addExn result.Exception
                        )

                    let projects =
                        builtProjects
                        |> Seq.map (fun p -> p.FullPath, ProjectLoader.getLoadedProjectInfo p.FullPath customProperties (ProjectLoader.StandardProject p))
                        |> Seq.choose (fun (projectPath, projectOptionResult) ->
                            match projectOptionResult with
                            | Ok projectOptions ->

                                Some projectOptions
                            | Error e ->
                                logger.error (
                                    Log.setMessage "Failed loading projects {error}"
                                    >> Log.addContextDestructured "error" e
                                )

                                loadingNotification.Trigger(WorkspaceProjectState.Failed(projectPath, GenericError(projectPath, e)))
                                None
                        )

                    let allProjectOptions =
                        projects
                        |> Seq.toList

                    let allStandardProjects =
                        allProjectOptions
                        |> List.choose (
                            function
                            | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> Some p
                            | _ -> None
                        )

                    allProjectOptions
                    |> List.iter (fun po ->
                        match po with
                        | ProjectLoader.LoadedProjectInfo.TraversalProjectInfo p ->
                            logger.info (
                                Log.setMessage "Traversal project loaded and contained the following references: {references}"
                                >> Log.addContextDestructured "references" p
                            )
                        | ProjectLoader.LoadedProjectInfo.StandardProjectInfo po ->
                            logger.info (
                                Log.setMessage "Project loaded {project}"
                                >> Log.addContextDestructured "project" po.ProjectFileName
                            )

                            loadingNotification.Trigger(WorkspaceProjectState.Loaded(po, allStandardProjects, false))
                        | ProjectLoader.LoadedProjectInfo.OtherProjectInfo p ->
                            logger.info (
                                Log.setMessage "Other project loaded {project}"
                                >> Log.addContextDestructured "project" p.FullPath
                            )

                    )

                    allStandardProjects :> seq<_>
        with e ->
            handleError "" e


    interface IWorkspaceLoader with
        override this.LoadProjects(projects: string list, customProperties, binaryLogs) =
            projectGraphProjects projects
            |> Option.map (fun pg -> loadProjects (pg, customProperties, binaryLogs))
            |> Option.defaultValue Seq.empty

        override this.LoadProjects(projects: string list) =
            this.LoadProjects(projects, [], BinaryLogGeneration.Off)

        override this.LoadSln(sln) =
            this.LoadSln(sln, [], BinaryLogGeneration.Off)

        override this.LoadSln(solutionPath: string, customProperties: string list, binaryLog: BinaryLogGeneration) =
            this.LoadSln(solutionPath, customProperties, binaryLog)

        [<CLIEvent>]
        override this.Notifications = loadingNotification.Publish

    member this.LoadProjects(projects: string list, customProperties: string list, binaryLogs) =
        (this :> IWorkspaceLoader).LoadProjects(projects, customProperties, binaryLogs)

    member this.LoadProjects(projects: string list, customProperties) =
        this.LoadProjects(projects, customProperties, BinaryLogGeneration.Off)


    member this.LoadProject(project: string, customProperties: string list, binaryLogs) =
        this.LoadProjects([ project ], customProperties, binaryLogs)

    member this.LoadProject(project: string, customProperties: string list) =
        this.LoadProjects([ project ], customProperties)

    member this.LoadProject(project: string) =
        (this :> IWorkspaceLoader).LoadProjects([ project ])


    member this.LoadSln(sln: string, customProperties: string list, binaryLogs) =
        projectGraphSln sln
        |> Option.map (fun pg -> loadProjects (pg, customProperties, binaryLogs))
        |> Option.defaultValue Seq.empty

    member this.LoadSln(sln, customProperties) =
        this.LoadSln(sln, customProperties, BinaryLogGeneration.Off)


    static member Create(toolsPath: ToolsPath, ?globalProperties) =
        WorkspaceLoaderViaProjectGraph(toolsPath, ?globalProperties = globalProperties) :> IWorkspaceLoader

type WorkspaceLoader private (toolsPath: ToolsPath, ?globalProperties: (string * string) list) =

    let projectCollection () =
        new ProjectCollection(
            globalProperties = dict (defaultArg globalProperties []),
            loggers = null,
            remoteLoggers = null,
            toolsetDefinitionLocations = ToolsetDefinitionLocations.Local,
            maxNodeCount = Environment.ProcessorCount,
            onlyLogCriticalEvents = false,
            loadProjectsReadOnly = true
        )

    let loadingNotification = new Event<Types.WorkspaceProjectState>()

    interface IWorkspaceLoader with

        [<CLIEvent>]
        override __.Notifications = loadingNotification.Publish

        override __.LoadProjects(projects: string list, customProperties, binaryLogs) =
            let cache = Dictionary<string, ProjectLoader.LoadedProjectInfo>()
            use per_request_collection = projectCollection ()

            let getAllKnown () =
                cache
                |> Seq.choose (fun (KeyValue(k, v)) ->
                    match v with
                    | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> Some p
                    | _ -> None
                )
                |> Seq.toList

            let rec loadProject p =

                match ProjectLoader.loadProject p binaryLogs per_request_collection with
                | Error msg when msg.Contains "The project file could not be loaded." ->
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotFound(p)))
                    [], None
                | Error msg when msg.Contains "not restored" ->
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, ProjectNotRestored(p)))
                    [], None
                | Error msg when msg.Contains "The operation cannot be completed because a build is already in progress." ->
                    //Try to load project again
                    Threading.Thread.Sleep(50)
                    loadProject p
                | Error msg ->
                    loadingNotification.Trigger(WorkspaceProjectState.Failed(p, GenericError(p, msg)))
                    [], None
                | Ok project ->
                    let mappedProjectInfo = ProjectLoader.getLoadedProjectInfo p customProperties project

                    match mappedProjectInfo with
                    | Ok project ->
                        cache.Add(p, project)

                        let referencedProjects =
                            match project with
                            | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> p.ReferencedProjects
                            | ProjectLoader.LoadedProjectInfo.TraversalProjectInfo p -> p
                            | ProjectLoader.LoadedProjectInfo.OtherProjectInfo p -> []

                        let lst =
                            referencedProjects
                            |> Seq.choose (fun n ->
                                if cache.ContainsKey n.ProjectFileName then
                                    None
                                else
                                    Some n.ProjectFileName
                            )
                            |> Seq.toList

                        let info =
                            match project with
                            | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> Some p
                            | ProjectLoader.LoadedProjectInfo.TraversalProjectInfo p -> None
                            | ProjectLoader.LoadedProjectInfo.OtherProjectInfo p -> None

                        lst, info
                    | Error msg ->
                        loadingNotification.Trigger(WorkspaceProjectState.Failed(p, GenericError(p, msg)))
                        [], None

            let rec loadProjectList (projectList: string list) =
                for p in projectList do
                    let newList, toTrigger =
                        if cache.ContainsKey p then
                            let project = cache.[p]

                            match project with
                            | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> loadingNotification.Trigger(WorkspaceProjectState.Loaded(p, getAllKnown (), true))
                            | ProjectLoader.LoadedProjectInfo.TraversalProjectInfo p -> ()
                            | ProjectLoader.LoadedProjectInfo.OtherProjectInfo p -> ()

                            let referencedProjects =
                                match project with
                                | ProjectLoader.LoadedProjectInfo.StandardProjectInfo p -> p.ReferencedProjects
                                | ProjectLoader.LoadedProjectInfo.TraversalProjectInfo p -> p
                                | ProjectLoader.LoadedProjectInfo.OtherProjectInfo p -> []

                            let lst =
                                referencedProjects
                                |> Seq.choose (fun n ->
                                    if cache.ContainsKey n.ProjectFileName then
                                        None
                                    else
                                        Some n.ProjectFileName
                                )
                                |> Seq.toList

                            lst, None
                        else
                            loadingNotification.Trigger(WorkspaceProjectState.Loading p)
                            loadProject p


                    loadProjectList newList


                    toTrigger
                    |> Option.iter (fun project -> loadingNotification.Trigger(WorkspaceProjectState.Loaded(project, getAllKnown (), false)))

            loadProjectList projects

            getAllKnown ()

        override this.LoadProjects(projects) =
            this.LoadProjects(projects, [], BinaryLogGeneration.Off)

        override this.LoadSln(sln) =
            this.LoadSln(sln, [], BinaryLogGeneration.Off)

        override this.LoadSln(solutionPath: string, customProperties: string list, binaryLog: BinaryLogGeneration) =
            this.LoadSln(solutionPath, customProperties, binaryLog)

    /// <inheritdoc />
    member this.LoadProjects(projects: string list, customProperties: string list, binaryLogs) =
        (this :> IWorkspaceLoader).LoadProjects(projects, customProperties, binaryLogs)

    member this.LoadProjects(projects, customProperties) =
        this.LoadProjects(projects, customProperties, BinaryLogGeneration.Off)


    member this.LoadProject(project, customProperties: string list, binaryLogs) =
        this.LoadProjects([ project ], customProperties, binaryLogs)

    member this.LoadProject(project, customProperties: string list) =
        this.LoadProjects([ project ], customProperties)

    member this.LoadProject(project) =
        (this :> IWorkspaceLoader).LoadProjects([ project ])


    member this.LoadSln(sln, customProperties: string list, binaryLogs) =
        match InspectSln.tryParseSln sln with
        | Ok(slnData) ->
            let solutionProjects = InspectSln.loadingBuildOrder slnData
            this.LoadProjects(solutionProjects, customProperties, binaryLogs)
        | Error d -> failwithf "Cannot load the sln: %A" d

    member this.LoadSln(sln, customProperties) =
        this.LoadSln(sln, customProperties, BinaryLogGeneration.Off)


    static member Create(toolsPath: ToolsPath, ?globalProperties) =
        WorkspaceLoader(toolsPath, ?globalProperties = globalProperties) :> IWorkspaceLoader

type ProjectViewerTree = {
    Name: string
    Items: ProjectViewerItem list
}

and [<RequireQualifiedAccess>] ProjectViewerItem = Compile of string * ProjectViewerItemConfig

and ProjectViewerItemConfig = { Link: string }

module ProjectViewer =

    let render (proj: ProjectOptions) =

        let compileFiles =
            let isCSharpProject = proj.ProjectFileName.EndsWith ".csproj"

            let sourceFilesExtension =
                if isCSharpProject then
                    "cs"
                else
                    "fs"

            let sources = proj.Items

            let projName =
                proj.ProjectFileName
                |> Path.GetFileNameWithoutExtension

            let normalize (path: string) =
                path.Replace("\\", "/").ToLowerInvariant()

            let assemblyAttributesName =
                let getProperty (name: string) =
                    proj.Properties
                    |> List.tryFind (fun p -> p.Name = name)
                    |> Option.map (fun p -> p.Value)

                let intermediateOutputPath = getProperty "IntermediateOutputPath"
                let tfi = proj.ProjectSdkInfo.TargetFrameworkIdentifier
                let tfv = proj.ProjectSdkInfo.TargetFrameworkVersion

                match intermediateOutputPath with
                | Some(iop: string) ->
                    Path.Combine(iop, $@"{tfi},Version={tfv}.AssemblyAttributes.{sourceFilesExtension}")
                    |> normalize
                    |> Some
                | _ -> None

            let isAssemblyAttributes (path: string) =
                match assemblyAttributesName with
                | Some assemblyAttributesName ->
                    path
                    |> normalize
                    |> (fun path -> path.EndsWith(assemblyAttributesName))
                | None -> false

            //The generated AssemblyInfo.fs are not shown as sources
            let isGeneratedAssemblyInfo (name: string) =
                //TODO check is in `obj` dir for the tfm
                //TODO better, get the name from fsproj
                name.EndsWith($"{projName}.AssemblyInfo.{sourceFilesExtension}")

            let includeSourceFile (name: string) =
                not (isAssemblyAttributes name)
                && not (isGeneratedAssemblyInfo name)

            sources
            |> List.choose (
                function
                | ProjectItem.Compile(name, fullPath, _) -> Some(name, fullPath)
            )
            |> List.filter (fun (_, p) -> includeSourceFile p)

        {
            ProjectViewerTree.Name =
                proj.ProjectFileName
                |> Path.GetFileNameWithoutExtension
            Items =
                compileFiles
                |> List.map (fun (name, fullPath) -> ProjectViewerItem.Compile(fullPath, { ProjectViewerItemConfig.Link = name }))
        }
