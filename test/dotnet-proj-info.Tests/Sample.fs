module DotnetProjInfo.Tests

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

let RepoDir = (__SOURCE_DIRECTORY__ /".." /"..") |> Path.GetFullPath
let TestRunDir = RepoDir/"test"/"testrun"
let NupkgsDir = RepoDir/"bin"/"nupkg"

let SamplePkgVersion = "1.0.0"
let SamplePkgDir = TestRunDir/"pkgs"/"SamplePkgDir"

let checkExitCodeZero (cmd: Command) =
    Expect.equal 0 cmd.Result.ExitCode "command finished with exit code non-zero."

let prepareTool (fs: FileUtils) pkgUnderTestVersion =
    fs.rm_rf (TestRunDir/"sdk2")
    fs.mkdir_p (TestRunDir/"sdk2")

    fs.cp (RepoDir/"test"/"usetool"/"tools.proj") (TestRunDir/"sdk2")
    fs.createFile (TestRunDir/"sdk2"/"nuget.config") (writeLines 
      [ "<configuration>"
        "  <packageSources>"
        sprintf """    <add key="local" value="%s" />""" NupkgsDir
        "  </packageSources>"
        "</configuration>" ])
    fs.createFile (TestRunDir/"sdk2"/"Directory.Build.props") (writeLines 
      [ """<Project ToolsVersion="15.0">"""
        "  <PropertyGroup>"
        sprintf """    <PkgUnderTestVersion>%s</PkgUnderTestVersion>""" pkgUnderTestVersion
        "  </PropertyGroup>"
        "</Project>" ])

    fs.cd (TestRunDir/"sdk2")
    fs.shellExecRun "dotnet" [ "restore"; "--packages"; "packages" ]
    |> checkExitCodeZero

let projInfo (fs: FileUtils) () =
    fs.cd (TestRunDir/"sdk2")
    fs.shellExecRun "dotnet" [ "proj-info"; "--help" ]

let copyDirFromAssets (fs: FileUtils) source outDir =
    fs.mkdir_p outDir

    // let path = nupkgReadonlyPath source
    // let sourceNupkgPath = outDir/(Path.GetFileName path)

    // fs.cp path sourceNupkgPath
    ()

let tests pkgUnderTestVersion =

  let prepareTestsAssets = lazy(
      let logger = Log.create "Tests Assets"
      let fs = FileUtils(logger)

      // restore tool
      prepareTool fs pkgUnderTestVersion
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

  [ 
    testList "general" [
      testCase |> withLog "can show help" (fun _ fs ->
        let testDir = inDir fs "args_help"

        projInfo fs ()
        |> checkExitCodeZero

      )
    ]

    testList "old sdk" [
      testCase |> withLog "can read properties" (fun _ fs ->
        let testDir = inDir fs "oldsdk_props"
        copyDirFromAssets fs ``samples1 OldSdk library`` testDir

        projInfo fs ()
        |> checkExitCodeZero
      )
    ]

  ]
  |> testList "suite"
