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

[<AutoOpen>]
module ExpectNotification =

  let (|IsLoading|_|) n =
    match n with
    | WorkspaceProjectState.Loading _ -> Some ()
    | _ -> None

  let (|IsLoaded|_|) n =
    match n with
    | WorkspaceProjectState.Loaded _ -> Some ()
    | _ -> None

  let (|IsFailed|_|) n =
    match n with
    | WorkspaceProjectState.Failed _ -> Some ()
    | _ -> None

  let loading = "loading", ( |IsLoading|_| ) >> Option.isSome
  let loaded = "loaded", ( |IsLoaded|_| ) >> Option.isSome
  let failed = "failed", ( |IsFailed|_| ) >> Option.isSome

  let expectNotifications actual expected =
    Expect.equal (List.length actual) (List.length expected) (sprintf "notifications: %A" (expected |> List.map fst))

    expected
    |> List.zip actual
    |> List.iter (fun (n, check) ->
        let name, f = check
        Expect.isTrue (f n) (sprintf "expected %s but was %A" name n) )

  type NotificationWatcher (loader: Dotnet.ProjInfo.Workspace.Loader, log) =
      let notifications = List<_>()

      do loader.Event1.Add(fun (_, arg) ->
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

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading; loaded ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 "lib"
        
        //TODO Configuration should be Debug
        //TODO TargetFramework should be `net461`
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "unknown"; TargetFramework = "v4.6.1" } "a lib"
      )

      testCase |> withLog "can load sample2" (fun logger fs ->
        let testDir = inDir fs "load_sample2"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading; loaded ]
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

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading; loading; loading; loaded ]
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

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        //TODO it notify a wrong additional loading of the cross targeting
        //     so should be [ loading; loaded ]

        [ loading; loading; loaded ]
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

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let watcher = watchNotifications logger loader

        loader.LoadProjects [projPath]

        [ loading; loaded ]
        |> expectNotifications (watcher.Notifications)

        let parsed = loader.Projects

        Expect.equal parsed.Length 1 "lib"
        
        Expect.equal (parsed |> findKey projPath) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "a C# lib"
      )

    ]

  [ valid ]
  |> testList "workspace"
  |> testSequenced
