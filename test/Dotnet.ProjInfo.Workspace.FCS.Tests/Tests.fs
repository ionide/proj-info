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
open Dotnet.ProjInfo.Workspace.FCS

#nowarn "25"

let RepoDir = (__SOURCE_DIRECTORY__ /".." /"..") |> Path.GetFullPath
let ExamplesDir = RepoDir/"test"/"examples"
let TestRunDir = RepoDir/"test"/"testrun_ws_fcs"
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

    let createFCS () =

      let checker =
        Microsoft.FSharp.Compiler.SourceCodeServices.FSharpChecker.Create(
          projectCacheSize = 200,
          keepAllBackgroundResolutions = true,
          keepAssemblyContents = true)

      checker.ImplicitlyStartBackgroundWork <- true

      checker

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

        let fcs = createFCS ()

        let loader = createLoader logger

        let fcsBinder = FCSBinder()

        fcsBinder.Bind(fcs, loader)
      )
    ]

  [ valid ]
  |> testList "workspace_fcs"
  |> testSequenced
