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

  let logNotification (logger: Logger) arg =
    logger.info(
      eventX "notified: {notification}'"
      >> setField "notification" arg)

  let valid =
    testList "valid" [

      testCase |> withLog "can load sample1" (fun _ fs ->
        let testDir = inDir fs "sanity_check_sample1"
        copyDirFromAssets fs ``samples1 OldSdk library``.ProjDir testDir

        Tests.skiptest "not yet implemented"

        let projPath = testDir/ (``samples1 OldSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        fs.cd projDir
        nuget fs ["restore"; "-PackagesDirectory"; "packages"]
        |> checkExitCodeZero

        fs.cd testDir
        msbuild fs [projPath; "/t:Build"]
        |> checkExitCodeZero
      )

      testCase |> withLog "can load sample2" (fun logger fs ->
        let testDir = inDir fs "sanity_check_sample2"
        copyDirFromAssets fs ``samples2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``samples2 NetSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        loader.Event1.Add(fun (_, arg) -> logNotification logger arg)

        loader.LoadProjects [projPath]

        let parsed = loader.Projects.ToArray()

        Expect.equal parsed.Length 1 "console and lib"
        
        Expect.equal (parsed.[0].Key) { ProjectKey.ProjectPath = projPath; Configuration = "Debug"; TargetFramework = "netstandard2.0" } "first is a lib"
      )

      testCase |> withLog "can load sample3" (fun logger fs ->
        let testDir = inDir fs "sanity_check_sample2"
        copyDirFromAssets fs ``sample3 Netsdk projs``.ProjDir testDir

        let projPath = testDir/ (``sample3 Netsdk projs``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let notifications = List<_>()

        loader.Event1.Add(fun (_, arg) ->
          notifications.Add(arg)
          logNotification logger arg)

        loader.LoadProjects [projPath]

        Expect.equal (notifications.Count) 3 "notifications: [loading; loading; loaded]"
      )

      testCase |> withLog "can load sample4" (fun logger fs ->
        let testDir = inDir fs "sanity_check_sample4"
        copyDirFromAssets fs ``samples4 NetSdk multi tfm``.ProjDir testDir

        let projPath = testDir/ (``samples4 NetSdk multi tfm``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        for (tfm, _) in ``samples4 NetSdk multi tfm``.TargetFrameworks |> Map.toList do
          printfn "tfm: %s" tfm

        let loader = Dotnet.ProjInfo.Workspace.Loader()

        let notifications = List<_>()

        loader.Event1.Add(fun (_, arg) ->
          notifications.Add(arg)
          logNotification logger arg)

        loader.LoadProjects [projPath]
      )

    ]

  [ valid ]
  |> testList "workspace"
  |> testSequenced
