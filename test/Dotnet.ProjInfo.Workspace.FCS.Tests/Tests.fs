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

type FCS_ProjectOptions = FSharp.Compiler.SourceCodeServices.FSharpProjectOptions
type FCS_Checker = FSharp.Compiler.SourceCodeServices.FSharpChecker
type FCS_Entity = FSharp.Compiler.SourceCodeServices.FSharpEntity
type FCS_Symbol = FSharp.Compiler.SourceCodeServices.FSharpSymbol
type FCS_CheckFileAnswer = FSharp.Compiler.SourceCodeServices.FSharpCheckFileAnswer
type FCS_CheckProjectResults = FSharp.Compiler.SourceCodeServices.FSharpCheckProjectResults

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

let logProjectOptions (logger: Logger) arg =
  logger.info(
    eventX "project options: {po}'"
    >> setField "po" (sprintf "%A" arg))
        
let expectFind key msg parsed =
  parsed
  |> Array.tryPick (fun (kv: KeyValuePair<ProjectKey, ProjectOptions>) ->
      if kv.Key = key then Some kv.Value else None)
  |> function
     | Some x -> x
     | None -> failwithf "%s. key '%A' not found in %A" msg key (parsed |> Array.map (fun kv -> kv.Key))

let expectNoErrors (result: FCS_CheckProjectResults) =
  Expect.isEmpty result.Errors (sprintf "no errors but was: %A" result.Errors)

let isOSX () =
  System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
      System.Runtime.InteropServices.OSPlatform.OSX)

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

  let valid =

    let createLoader logger =
        let msbuildLocator = MSBuildLocator()
        let config = LoaderConfig.Default msbuildLocator
        logConfig logger config
        let loader = Loader.Create(config)

        let netFwconfig = NetFWInfoConfig.Default msbuildLocator
        logConfig logger netFwconfig
        let netFwInfo = NetFWInfo.Create(netFwconfig)

        loader, netFwInfo

    let createFCS () =

      let checker =
        FCS_Checker.Create(
          projectCacheSize = 200,
          keepAllBackgroundResolutions = true,
          keepAssemblyContents = true)

      checker.ImplicitlyStartBackgroundWork <- true

      checker

    let createNetFwInfo logger =
        let msbuildLocator = MSBuildLocator()
        let config = NetFWInfoConfig.Default msbuildLocator
        logConfig logger config
        let netFwInfo = NetFWInfo.Create(config)
        netFwInfo

    testList "valid" [

      testCase |> withLog "can load sample1" (fun logger fs ->
        let testDir = inDir fs "load_sample1"
        copyDirFromAssets fs ``sample1 OldSdk library``.ProjDir testDir

        let projPath = testDir/ (``sample1 OldSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        fs.cd projDir
        nuget fs ["restore"; "-PackagesDirectory"; "packages"]
        |> checkExitCodeZero

        fs.cd testDir

        // msbuild fs [projPath; "/t:Build"]
        // |> checkExitCodeZero

        let fcs = createFCS ()

        let loader, netFwInfo = createLoader logger

        let fcsBinder = FCSBinder(netFwInfo, loader, fcs)

        loader.LoadProjects [ projPath ]

        let fcsPoOpt = fcsBinder.GetProjectOptions(projPath)

        logProjectOptions logger fcsPoOpt

        let fcsPo = fcsPoOpt |> Option.get

        let po =
          loader.Projects
          |> expectFind { ProjectKey.ProjectPath = projPath; TargetFramework = "net461" } "find proj"

        Expect.equal fcsPo.LoadTime po.LoadTime "load time"
        Expect.equal fcsPo.ReferencedProjects.Length po.ReferencedProjects.Length "refs"
        Expect.equal fcsPo.ExtraProjectInfo (Some (box po)) "extra info"

        //TODO check fullpaths
        Expect.equal fcsPo.SourceFiles [| projDir/"AssemblyInfo.fs"; projDir/"Library.fs" |] "check sourcefiles"

        let result =
          fcs.ParseAndCheckProject(fcsPo)
          |> Async.RunSynchronously

        expectNoErrors result

        let uses =
          result.GetAllUsesOfAllSymbols()
          |> Async.RunSynchronously

        Expect.isNonEmpty uses "all symbols usages"
      )

      testCase |> withLog "do not include generated tfm assemblyinfo" (fun logger fs ->
        let testDir = inDir fs "load_sample1"
        copyDirFromAssets fs ``sample1 OldSdk library``.ProjDir testDir

        let projPath = testDir/ (``sample1 OldSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        fs.cd projDir
        nuget fs ["restore"; "-PackagesDirectory"; "packages"]
        |> checkExitCodeZero

        fs.cd testDir

        let fcs = createFCS ()

        let loader, netFwInfo = createLoader logger

        let fcsBinder = FCSBinder(netFwInfo, loader, fcs)

        loader.LoadProjects [ projPath ]

        let fcsPoOpt = fcsBinder.GetProjectOptions(projPath)

        logProjectOptions logger fcsPoOpt

        let fcsPo = fcsPoOpt |> Option.get

        Expect.isFalse (fcsPo.SourceFiles |> Array.contains (Path.GetTempPath()/".NETFramework,Version=v4.6.1.AssemblyAttributes.fs")) (sprintf "check doesnt exists the generated tfm assemblyinfo file, but was %A" fcsPo.SourceFiles)
        Expect.equal fcsPo.SourceFiles [| projDir/"AssemblyInfo.fs"; projDir/"Library.fs" |] "check exact sourcefiles"
      )

      testCase |> withLog "can load sample2" (fun logger fs ->
        let testDir = inDir fs "load_sample2"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir/ (``sample2 NetSdk library``.ProjectFile)

        dotnet fs ["restore"; projPath]
        |> checkExitCodeZero

        let fcs = createFCS ()

        let loader, netFwInfo = createLoader logger

        let fcsBinder = FCSBinder(netFwInfo, loader, fcs)

        loader.LoadProjects [projPath]

        let fcsPoOpt = fcsBinder.GetProjectOptions(projPath)

        logProjectOptions logger fcsPoOpt

        let fcsPo = fcsPoOpt |> Option.get

        let po =
          loader.Projects
          |> expectFind { ProjectKey.ProjectPath = projPath; TargetFramework = "netstandard2.0" } "first is a lib"

        Expect.equal fcsPo.LoadTime po.LoadTime "load time"
        Expect.equal fcsPo.ReferencedProjects.Length po.ReferencedProjects.Length "refs"
        Expect.equal fcsPo.ExtraProjectInfo (Some (box po)) "extra info"

        //TODO check fullpaths
        Expect.equal fcsPo.SourceFiles (po.SourceFiles |> Array.ofList) "check sources"

        let result =
          fcs.ParseAndCheckProject(fcsPo)
          |> Async.RunSynchronously

        expectNoErrors result

        let uses =
          result.GetAllUsesOfAllSymbols()
          |> Async.RunSynchronously

        Expect.isNonEmpty uses "all symbols usages"

      )

      testCase |> withLog "can load sample3" (fun logger fs ->
        let testDir = inDir fs "load_sample3"
        copyDirFromAssets fs ``sample3 Netsdk projs``.ProjDir testDir

        let projPath = testDir/ (``sample3 Netsdk projs``.ProjectFile)

        // for no errors, use build instead of restore because there is a C# lib.
        dotnet fs ["build"; projPath]
        |> checkExitCodeZero

        let fcs = createFCS ()

        let loader, netFwInfo = createLoader logger

        let fcsBinder = FCSBinder(netFwInfo, loader, fcs)

        loader.LoadProjects [projPath]

        let fcsPoOpt = fcsBinder.GetProjectOptions(projPath)

        logProjectOptions logger fcsPoOpt

        let fcsPo = fcsPoOpt |> Option.get

        let po =
          loader.Projects
          |> expectFind { ProjectKey.ProjectPath = projPath; TargetFramework = "netcoreapp2.1" } "first is a console app"

        Expect.equal fcsPo.LoadTime po.LoadTime "load time"
        Expect.equal fcsPo.ReferencedProjects.Length (po.ReferencedProjects.Length - 1) "refs" // one is C#, no FSharpProjectOptions for that
        Expect.equal fcsPo.ExtraProjectInfo (Some (box po)) "extra info"

        //TODO check fullpaths
        Expect.equal fcsPo.SourceFiles (po.SourceFiles |> Array.ofList) "check sources"

        let result =
          fcs.ParseAndCheckProject(fcsPo)
          |> Async.RunSynchronously

        if (isOSX ()) then
          let errorOnOsx =
            """
no errors but was: [|commandLineArgs (0,1)-(0,1) parameter error No inputs specified;
 unknown (1,1)-(1,1) parameter error Assembly reference 'mscorlib.dll' was not found or is invalid|]. Should be empty.
            """.Trim()
          Tests.skiptest (sprintf "Known failure on OSX travis. error is %s" errorOnOsx)
          //TODO check failure on osx
          //the same sample3 workspace test fails, OtherOptions is empty
        else
          expectNoErrors result

        let uses =
          result.GetAllUsesOfAllSymbols()
          |> Async.RunSynchronously

        Expect.isNonEmpty uses "all symbols usages"

      )

      testCase |> withLog "can fsx" (fun logger fs ->
        let testDir = inDir fs "check_fsx"

        let fcs = createFCS ()

        let loader, netFwInfo = createLoader logger

        let tfm = netFwInfo.LatestVersion ()

        let fsxBinder = FsxBinder(netFwInfo, fcs)

        let file = "a.fsx"
        let input =
          """
let foo = 1+1"
          """

        //TODO fsharp.core is wrong, is netstandard1.6
        //TODO parametrize fsharp.core
        let projOptions =
          fsxBinder.GetProjectOptionsFromScriptBy(tfm, file, input)
          |> Async.RunSynchronously

        logProjectOptions logger projOptions

        let result =
          fcs.ParseAndCheckProject(projOptions)
          |> Async.RunSynchronously

        expectNoErrors result

        let parseFileResults, checkFileResults = 
            fcs.ParseAndCheckFileInProject(file, 0, input, projOptions) 
            |> Async.RunSynchronously

        let res =
          match checkFileResults with
          | FCS_CheckFileAnswer.Succeeded(res) -> res
          | res -> failwithf "Parsing did not finish... (%A)" res

        let partialAssemblySignature = res.PartialAssemblySignature
            
        Expect.equal partialAssemblySignature.Entities.Count 1 "one entity"
            
        let moduleEntity = partialAssemblySignature.Entities.[0]

        Expect.equal moduleEntity.MembersFunctionsAndValues.Count 1 "one function"

        let fnVal = moduleEntity.MembersFunctionsAndValues.[0]

        Expect.equal fnVal.DisplayName "foo" "exists function foo"
      )
    ]

  [ valid ]
  |> testList "workspace_fcs"
  |> testSequenced
