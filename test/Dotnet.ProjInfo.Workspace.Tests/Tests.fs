module Tests

open System
open System.IO
open Expecto
open Expecto.Logging
open Expecto.Logging.Message
open FileUtils
open Medallion.Shell
open System.IO.Compression
open System.Xml.Linq
open DotnetProjInfo.TestAssets
open System.Collections.Generic
open Dotnet.ProjInfo.Workspace
open System.Collections.Generic

#nowarn "25"

let RepoDir = (__SOURCE_DIRECTORY__ /".." /"..") |> Path.GetFullPath
let ExamplesDir = RepoDir/"test"/"examples"
let TestRunDir = RepoDir/"test"/"testrun_ws"
let TestRunInvariantDir = TestRunDir/"invariant"

let checkExitCodeZero (cmd: Command) =
    Expect.equal 0 cmd.Result.ExitCode "command finished with exit code non-zero."

let renderNugetConfig clear feeds =
    [ yield "<configuration>"
      yield "  <packageSources>"
      if clear then
        yield "    <clear />"
      for (name, url) in feeds do
        yield sprintf """    <add key="%s" value="%s" />""" name url
      yield "  </packageSources>"
      yield "</configuration>" ]

let prepareTool (fs: FileUtils) =

    for dir in [TestRunInvariantDir] do
      fs.rm_rf dir
      fs.mkdir_p dir

    fs.cd TestRunInvariantDir

let dotnet (fs: FileUtils) args =
    fs.shellExecRun "dotnet" args

let msbuild (fs: FileUtils) args =
    fs.shellExecRun "msbuild" args

let nuget (fs: FileUtils) args =
    fs.shellExecRunNET (TestRunDir/"nuget"/"nuget.exe") args

let copyDirFromAssets (fs: FileUtils) source outDir =
    fs.mkdir_p outDir

    let path = ExamplesDir/source

    fs.cp_r path outDir
    ()

let downloadNugetClient (logger: Logger) (nugetUrl: string) nugetPath =
    if not(File.Exists(nugetPath)) then
      logger.info(
        eventX "download of nuget.exe from {url} to '{path}'"
        >> setField "url" nugetUrl
        >> setField "path" nugetPath)
      let wc = new System.Net.WebClient()
      mkdir_p logger (Path.GetDirectoryName(nugetPath))
      wc.DownloadFile(nugetUrl, nugetPath)
    else
      logger.info(
        eventX "nuget.exe already found in '{path}'"
        >> setField "path" nugetPath)

let logNotification (logger: Logger) arg =
  logger.info(
    eventX "notified: {notification}'"
    >> setField "notification" arg)

let logConfig (logger: Logger) arg =
  logger.info(
    eventX "config: {config}'"
    >> setField "config" arg)

let logMsbuild (logger: Logger) arg =
  logger.info(
    eventX "msbuild: {msbuild}'"
    >> setField "msbuild" arg)

[<AutoOpen>]
module ExpectNotification =

  let loading (name: string) =
    let isLoading n =
      match n with
      | WorkspaceProjectState.Loading (path, _) when path.EndsWith(name) -> true
      | _ -> false
    sprintf "loading %s" name, isLoading

  let loaded (name: string) =
    let isLoaded n =
      match n with
      | WorkspaceProjectState.Loaded (po, _, _) when po.ProjectFileName.EndsWith(name) -> true
      | _ -> false
    sprintf "loaded %s" name, isLoaded

  let failed (name: string) =
    let isFailed n =
      match n with
      | WorkspaceProjectState.Failed (path, _) when path.EndsWith(name) -> true
      | _ -> false
    sprintf "failed %s" name, isFailed

  let expectNotifications actual expected =
    Expect.equal (List.length actual) (List.length expected) (sprintf "notifications: %A" (expected |> List.map fst))

    expected
    |> List.zip actual
    |> List.iter (fun (n, check) ->
        let name, f = check
        let minimal_info =
          match n with
          | WorkspaceProjectState.Loading (path, _) -> sprintf "loading %s " path
          | WorkspaceProjectState.Loaded (po, _, _) -> sprintf "loaded %s" po.ProjectFileName
          | WorkspaceProjectState.Failed (path, _) -> sprintf "failed %s" path
        Expect.isTrue (f n) (sprintf "expected %s but was %s" name minimal_info) )

  type NotificationWatcher (loader: Dotnet.ProjInfo.Workspace.Loader, log) =
      let notifications = List<_>()

      do loader.Notifications.Add(fun (_, arg) ->
            notifications.Add(arg)
            log arg)

      member __.Notifications
          with get () = notifications |> List.ofSeq
        
let findKey path parsed =
  parsed
  |> Array.tryPick (fun (kv: KeyValuePair<ProjectKey, ProjectOptions>) ->
      if kv.Key.ProjectPath = path then Some kv.Key else None)
  |> function
     | Some x -> x
     | None -> failwithf "key '%s' not found in %A" path (parsed |> Array.map (fun kv -> kv.Key))

let tests () =
 
  let prepareTestsAssets = lazy(
      let logger = Log.create "Tests Assets"
      let fs = FileUtils(logger)

      // restore tool
      prepareTool fs

      // download nuget client
      let nugetUrl = "https://dist.nuget.org/win-x86-commandline/v4.7.1/nuget.exe"
      let nugetPath = TestRunDir/"nuget"/"nuget.exe"
      downloadNugetClient logger nugetUrl nugetPath
    )

  let withLog name f test =
    test name (fun () ->
      prepareTestsAssets.Force()

      let logger = Log.create (sprintf "Test '%s'" name)
      let fs = FileUtils(logger)
      f logger fs)

  let withLogAsync name f test =
    test name (async {
      prepareTestsAssets.Force()

      let logger = Log.create (sprintf "Test '%s'" name)
      let fs = FileUtils(logger)
      do! f logger fs })

  let inDir (fs: FileUtils) dirName =
    let outDir = TestRunDir/dirName
    fs.rm_rf outDir
    fs.mkdir_p outDir
    fs.cd outDir
    outDir

  let asLines (s: string) =
    s.Split(Environment.NewLine) |> List.ofArray

  let stdOutLines (cmd: Command) =
    cmd.Result.StandardOutput
    |> fun s -> s.Trim()
    |> asLines

  let watchNotifications logger loader =
     NotificationWatcher (loader, logNotification logger)

  let valid =

    let createLoader logger =
        let msbuildLocator = MSBuildLocator()
        let config = LoaderConfig.Default msbuildLocator
        logConfig logger config
        let loader = Loader.Create(config)
        loader

    testList "valid" [

      testCase |> withLog "can load sample1" (fun logger fs ->
        let testDir = inDir fs "load_sample1"
        copyDirFromAssets fs ``samples1 OldSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples1 OldSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        fs.cd projDir
        nuget fs ["restore"; "-PackagesDirectory"; "packages"]
        |> checkExitCodeZero

        fs.cd testDir

        // msbuild fs [projPath; "/t:Build"]
        // |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading "l1.fsproj"; loaded "l1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 "lib"
        
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "net461" } "a lib"
      )

      testCase |> withLog "can load sample2" (fun logger fs ->
        let testDir = inDir fs "load_sample2"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading "n1.fsproj"; loaded "n1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 "console and lib"
        
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "first is a lib"
      )

      testCase |> withLog "can load sample3" (fun logger fs ->
        let testDir = inDir fs "load_sample3"
        copyDirFromAssets fs ``sample3 Netsdk projs``.ProjDir testDir

        let projPath = testDir/ (``sample3 Netsdk projs``.ProjectFile)
        let l1 :: l2 :: [] =
          ``sample3 Netsdk projs``.ProjectReferences
          |> List.map (fun p2p -> testDir/ p2p.ProjectFile )

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading "c1.fsproj"; loading "l1.csproj"; loading "l2.fsproj"; loaded "c1.fsproj"; loaded "l1.csproj"; loaded "l2.fsproj";  ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 3 (sprintf "console (F#) and lib (F#) and lib (C#), but was %A" (parsed |> Array.map (fun x -> x.Key)))

        Expect.equal (parsed |> findKey l1) { ProjectKey.ProjectPath = l1; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "the F# lib"
        Expect.equal (parsed |> findKey l2) { ProjectKey.ProjectPath = l2; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "the C# lib"
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netcoreapp2.1" } "the F# console"
      )

      testCase |> withLog "can load sample4" (fun logger fs ->
        let testDir = inDir fs "load_sample4"
        copyDirFromAssets fs ``samples4 NetSdk multi tfm``.ProjDir testDir

        let projPath = testDir/ (``samples4 NetSdk multi tfm``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        for (tfm, _) in ``samples4 NetSdk multi tfm``.TargetFrameworks |> Map.toList do
          printfn "tfm: %s" tfm

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        //the additional loading is the cross targeting
        [ loading "m1.fsproj"; loading "m1.fsproj"; loaded "m1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 (sprintf "multi-tfm lib (F#), but was %A" (parsed |> Array.map (fun x -> x.Key)))

        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "the F# console"
      )

      testCase |> withLog "can load sample5" (fun logger fs ->
        let testDir = inDir fs "load_sample5"
        copyDirFromAssets fs ``samples5 NetSdk CSharp library``.ProjDir testDir

        let projPath = testDir/ (``samples5 NetSdk CSharp library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading "l2.csproj"; loaded "l2.csproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 "lib"
        
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "a C# lib"
      )

      testCase |> withLog "can load sln" (fun logger fs ->
        let testDir = inDir fs "load_sln"
        copyDirFromAssets fs ``sample6 Netsdk Sparse/sln``.ProjDir testDir

        let slnPath = testDir/ (``sample6 Netsdk Sparse/sln``.ProjectFile)

        dotnet fs ["restore"; slnPath]
        |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        loader.LoadSln(slnPath)

        //TODO to check: l2 is loaded from cache, but no loading notification
        [ loading "c1.fsproj"; loading "l2.fsproj"; loaded "c1.fsproj"; loaded "l2.fsproj"; loading "l1.fsproj"; loaded "l1.fsproj"; loaded "l2.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 3 "c1, l1, l2"
        
        let c1 = testDir/ (``sample6 Netsdk Sparse/1``.ProjectFile)
        let l2 :: [] =
          ``sample6 Netsdk Sparse/1``.ProjectReferences
          |> List.map (fun p2p -> testDir/ p2p.ProjectFile )
        let l1 = testDir/ (``sample6 Netsdk Sparse/2``.ProjectFile)

        Expect.equal (parsed |> findKey l1) { ProjectKey.ProjectPath = l1; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "the F# lib"
        Expect.equal (parsed |> findKey l2) { ProjectKey.ProjectPath = l2; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "the C# lib"
        Expect.equal (parsed |> findKey c1) { ProjectKey.ProjectPath = c1; Configuration = "Debug"; TargetFramework = "netcoreapp2.1" } "the F# console"
      )

    ]

  let invalid =

    let createLoader logger =
        let msbuildLocator = MSBuildLocator()
        let config = LoaderConfig.Default msbuildLocator
        logConfig logger config
        let loader = Loader.Create(config)
        loader

    testList "invalid" [

      testCase |> withLog "project not found" (fun logger fs ->
        let testDir = inDir fs "proj_not_found"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = createLoader logger

        let watcher = watchNotifications logger loader

        let wrongPath =
          let dir, name, ext = Path.GetDirectoryName projPath, Path.GetFileNameWithoutExtension projPath, Path.GetExtension projPath
          Path.Combine(dir, name + "aa" + ext)

        loader.LoadProjects [wrongPath]

        [ loading "n1aa.fsproj"; failed "n1aa.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 0 "no project loaded"
        
        Expect.equal (watcher.Notifications |> List.item 1) (WorkspaceProjectState.Failed(wrongPath, (GetProjectOptionsErrors.GenericError(wrongPath, "not found")))) "check error type"
      )

      testCase |> withLog "project not restored" (fun logger fs ->
        let testDir = inDir fs "proj_not_restored"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        let loader = createLoader logger

        // no restore

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading "n1.fsproj"; failed "n1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 0 "no project loaded"
        
        Expect.equal (watcher.Notifications |> List.item 1) (WorkspaceProjectState.Failed(projPath, (GetProjectOptionsErrors.ProjectNotRestored projPath))) "check error type"
      )
    ]

  let fsx =

    let isAssembly (name: string) (tfm: string) (path: string) =
        path.EndsWith(name)
        && (
          // TODO do not use paths, check with cecil the assemblyversion
          path.Contains(sprintf @"\v%s\" tfm) // win
          || path.Contains(sprintf "/%s/" tfm) // mono
          || path.Contains(sprintf @"/%s-api/" tfm) ) // mono

    let createNetFwInfo logger =
        let msbuildLocator = MSBuildLocator()
        let config = NetFWInfoConfig.Default msbuildLocator
        logConfig logger config
        let netFwInfo = NetFWInfo.Create(config)
        netFwInfo

    testList "fsx" [

      testCase |> withLog "fsx args" (fun logger fs ->
        let testDir = inDir fs "fsx_args"

        let dummy (file:string, source:string, additionaRefs: string array, assumeDotNetFramework:bool) = async {
            printfn "%A" additionaRefs

            Expect.equal file "a.fsx" "filename"
            Expect.equal source "text content" "filename"
            Expect.equal assumeDotNetFramework true "hardcoded value"
            Expect.exists additionaRefs (isAssembly "mscorlib.dll" "4.6.1") "check net461 exists"

            return (1,2)
        }

        let netFw = createNetFwInfo logger

        let a, mapper =
          netFw.GetProjectOptionsFromScript(dummy, "v4.6.1", "a.fsx", "text content")
          |> Async.RunSynchronously

        Expect.equal a 1 "returned"

        let _changed = mapper [| "a"; "b" |]

        ()
      )
    ]

  let netfw =

    let createNetFwInfo logger =
        let msbuildLocator = MSBuildLocator()
        let config = NetFWInfoConfig.Default msbuildLocator
        logConfig logger config
        let netFwInfo = NetFWInfo.Create(config)
        netFwInfo

    testList "netfw" [

      testCase |> withLog "installed .net fw" (fun logger fs ->
        let testDir = inDir fs "netfw"

        let netFw = createNetFwInfo logger

        let fws = netFw.InstalledNetFws()

        printfn "fws: %A" fws

        Expect.contains fws "v4.6.1" "installed .net fw"
      )
    ]

  let msbuild =

    testList "msbuild" [

      testCase |> withLog "installed msbuild" (fun logger fs ->
        let testDir = inDir fs "msbuild_installed"

        let msbuildLocator = MSBuildLocator()

        let msbuildPaths = msbuildLocator.InstalledMSBuilds ()

        logMsbuild logger msbuildPaths

        Expect.isNonEmpty msbuildPaths "paths"

        let msbuildPath = msbuildLocator.LatestInstalledMSBuild ()

        logMsbuild logger msbuildPath

        match msbuildPath with
        | Dotnet.ProjInfo.Inspect.MSBuildExePath.Path path ->
          Expect.isNotEmpty path "path"
        | Dotnet.ProjInfo.Inspect.MSBuildExePath.DotnetMsbuild p ->
          failwithf "expected msbuild, not 'dotnet %s'" p
      )
    ]

  [ valid; invalid; fsx; netfw; msbuild ]
  |> testList "workspace"
  |> testSequenced
