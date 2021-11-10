module Tests

open Expecto
open FileUtils
open Medallion.Shell
open System.IO
open Expecto.Logging
open DotnetProjInfo.TestAssets
open Ionide.ProjInfo
open System.Collections.Generic
open Ionide.ProjInfo.Types
open Ionide.ProjInfo
open Expecto.Logging.Message
open FSharp.Compiler.CodeAnalysis

#nowarn "25"

let RepoDir = (__SOURCE_DIRECTORY__ / ".." / "..") |> Path.GetFullPath
let ExamplesDir = RepoDir / "test" / "examples"
let TestRunDir = RepoDir / "test" / "testrun_ws"
let TestRunInvariantDir = TestRunDir / "invariant"

let checkExitCodeZero (cmd: Command) =
    Expect.equal 0 cmd.Result.ExitCode "command finished with exit code non-zero."

let findByPath path parsed =
    parsed
    |> Array.tryPick (fun (kv: KeyValuePair<string, ProjectOptions>) ->
        if kv.Key = path then
            Some kv
        else
            None)
    |> function
        | Some x -> x
        | None -> failwithf "key '%s' not found in %A" path (parsed |> Array.map (fun kv -> kv.Key))

let expectFind projPath msg (parsed: ProjectOptions list) =
    let p = parsed |> List.tryFind (fun n -> n.ProjectFileName = projPath)
    Expect.isSome p msg
    p.Value


let inDir (fs: FileUtils) dirName =
    let outDir = TestRunDir / dirName
    fs.rm_rf outDir
    fs.mkdir_p outDir
    fs.cd outDir
    outDir

let copyDirFromAssets (fs: FileUtils) source outDir =
    fs.mkdir_p outDir

    let path = ExamplesDir / source

    fs.cp_r path outDir
    ()

let dotnet (fs: FileUtils) args = fs.shellExecRun "dotnet" args

let withLog name f test =
    test name (fun () ->

        let logger = Log.create (sprintf "Test '%s'" name)
        let fs = FileUtils(logger)
        f logger fs)

let renderOf sampleProj sources =
    { ProjectViewerTree.Name = sampleProj.ProjectFile |> Path.GetFileNameWithoutExtension
      Items = sources |> List.map (fun (path, link) -> ProjectViewerItem.Compile(path, { ProjectViewerItemConfig.Link = link })) }

let createFCS () =
    let checker = FSharpChecker.Create(projectCacheSize = 200, keepAllBackgroundResolutions = true, keepAssemblyContents = true)
    checker

let sleepABit () =
    // we wait a bit longer on macos in CI due to apparent slowness
    if System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX) then
        System.Threading.Thread.Sleep 5000
    else
        System.Threading.Thread.Sleep 3000

[<AutoOpen>]
module ExpectNotification =

    let loading (name: string) =
        let isLoading n =
            match n with
            | WorkspaceProjectState.Loading (path) when path.EndsWith(name) -> true
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

        let getMessages =
            function
            | WorkspaceProjectState.Loading (path) -> sprintf "loading %s " path
            | WorkspaceProjectState.Loaded (po, _, _) -> sprintf "loaded %s" po.ProjectFileName
            | WorkspaceProjectState.Failed (path, _) -> sprintf "failed %s" path

        Expect.equal (List.length actual) (List.length expected) (sprintf "expected notifications: %A \n actual notifications: - %A" (expected |> List.map fst) (actual |> List.map getMessages))

        expected
        |> List.iter (fun (name, f) ->
            let item = actual |> List.tryFind (fun a -> f a)
            let minimal_info = item |> Option.map getMessages |> Option.defaultValue ""
            Expect.isSome item (sprintf "expected %s but was %s" name minimal_info))


    type NotificationWatcher(loader: Ionide.ProjInfo.IWorkspaceLoader, log) =
        let notifications = List<_>()

        do
            loader.Notifications.Add (fun arg ->
                notifications.Add(arg)
                log arg)

        member __.Notifications = notifications |> List.ofSeq

    let logNotification (logger: Logger) arg =
        logger.debug (eventX "notified: {notification}'" >> setField "notification" arg)

    let watchNotifications logger loader =
        NotificationWatcher(loader, logNotification logger)

let testLegacyFrameworkProject toolsPath workspaceLoader isRelease (workspaceFactory: ToolsPath * (string * string) list -> IWorkspaceLoader) =
    testCase
    |> withLog
        (sprintf "can load legacy project - %s - isRelease is %b" workspaceLoader isRelease)
        (fun logger fs ->

            let testDir = inDir fs "a"
            copyDirFromAssets fs ``sample7 legacy framework project``.ProjDir testDir

            let projPath = testDir / (``sample7 legacy framework project``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            let config =
                if isRelease then
                    "Release"
                else
                    "Debug"

            let props = [ ("Configuration", config) ]
            let loader = workspaceFactory (toolsPath, props)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            [ loading projPath
              loaded projPath ]
            |> expectNotifications watcher.Notifications

            let [ _; WorkspaceProjectState.Loaded (n1Loaded, _, _) ] = watcher.Notifications

            let n1Parsed = parsed |> expectFind projPath "first is a lib"

            let expectedSources =
                [ projDir / "Project1A.fs" ]
                |> List.map Path.GetFullPath

            Expect.equal parsed.Length 1 "console and lib"
            Expect.equal n1Parsed n1Loaded "notificaton and parsed should be the same"
            Expect.equal n1Parsed.SourceFiles expectedSources "check sources"
            )

let testLegacyFrameworkMultiProject toolsPath workspaceLoader isRelease (workspaceFactory: ToolsPath * (string * string) list -> IWorkspaceLoader) =
    testCase
    |> withLog
        (sprintf "can load legacy project - %s - isRelease is %b" workspaceLoader isRelease)
        (fun logger fs ->

            let testDir = inDir fs "load_sample7"
            copyDirFromAssets fs ``sample7 legacy framework multi-project``.ProjDir testDir

            let projPath = testDir / (``sample7 legacy framework multi-project``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            let [ (l1, l1Dir); (l2, l2Dir) ] =
                ``sample7 legacy framework multi-project``.ProjectReferences
                |> List.map (fun p2p -> testDir / p2p.ProjectFile)
                |> List.map Path.GetFullPath
                |> List.map (fun path -> path, Path.GetDirectoryName(path))

            let config =
                if isRelease then
                    "Release"
                else
                    "Debug"

            let props = [ ("Configuration", config) ]
            let loader = workspaceFactory (toolsPath, props)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList
            [ loading projPath
              loading l1
              loaded l1
              loading l2
              loaded l2
              loaded projPath
             ]
            |> expectNotifications watcher.Notifications

            let [ _; _; WorkspaceProjectState.Loaded (l1Loaded, _, _); _; WorkspaceProjectState.Loaded (l2Loaded, _, _); WorkspaceProjectState.Loaded (n1Loaded, _, _) ] =
                      watcher.Notifications

            let n1Parsed = parsed |> expectFind projPath "first is a multi-project"
            let n1ExpectedSources =
                      [ projDir / "MultiProject1.fs"]
                      |> List.map Path.GetFullPath

            let l1Parsed = parsed |> expectFind l1 "the F# lib"
            let l1ExpectedSources =
                [ l1Dir / "Project1A.fs"]
                |> List.map Path.GetFullPath

            let l2Parsed = parsed |> expectFind l2 "the F# exe"
            let l2ExpectedSources =
                [ l2Dir / "Project1B.fs"]
                |> List.map Path.GetFullPath

            Expect.equal parsed.Length 3 "check whether all projects in the multi-project were loaded"
            Expect.equal n1Parsed.SourceFiles n1ExpectedSources "check sources - N1"
            Expect.equal l1Parsed.SourceFiles l1ExpectedSources "check sources - L1"
            Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources - L2"

            Expect.equal l1Parsed l1Loaded "l1 notificaton and parsed should be the same"
            Expect.equal l2Parsed l2Loaded "l2 notificaton and parsed should be the same"
            Expect.equal n1Parsed n1Loaded "n1 notificaton and parsed should be the same"
            )

let testSample2 toolsPath workspaceLoader isRelease (workspaceFactory: ToolsPath * (string * string) list -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample2 - %s - isRelease is %b" workspaceLoader isRelease) (fun logger fs ->
        let testDir = inDir fs "load_sample2"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let config =
            if isRelease then
                "Release"
            else
                "Debug"

        let props = [ ("Configuration", config) ]
        let loader = workspaceFactory (toolsPath, props)

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        [ loading "n1.fsproj"
          loaded "n1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let [ _; WorkspaceProjectState.Loaded (n1Loaded, _, _) ] = watcher.Notifications

        let n1Parsed = parsed |> expectFind projPath "first is a lib"

        let expectedSources =
            [ projDir / ("obj/" + config + "/netstandard2.0/n1.AssemblyInfo.fs")
              projDir / "Library.fs"
              if isRelease then
                  projDir / "Other.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal parsed.Length 1 "console and lib"
        Expect.equal n1Parsed n1Loaded "notificaton and parsed should be the same"
        Expect.equal n1Parsed.SourceFiles expectedSources "check sources")

let testSample3 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) expected =
    testCase
    |> withLog (sprintf "can load sample3 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample3"
        copyDirFromAssets fs ``sample3 Netsdk projs``.ProjDir testDir

        let projPath = testDir / (``sample3 Netsdk projs``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        let [ (l1, l1Dir); (l2, l2Dir) ] =
            ``sample3 Netsdk projs``.ProjectReferences
            |> List.map (fun p2p -> testDir / p2p.ProjectFile)
            |> List.map Path.GetFullPath
            |> List.map (fun path -> path, Path.GetDirectoryName(path))

        dotnet fs [ "build"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        expected |> expectNotifications (watcher.Notifications)


        let [ _; _; WorkspaceProjectState.Loaded (l1Loaded, _, _); _; WorkspaceProjectState.Loaded (l2Loaded, _, _); WorkspaceProjectState.Loaded (c1Loaded, _, _) ] =
            watcher.Notifications



        let l1Parsed = parsed |> expectFind l1 "the C# lib"

        let l1ExpectedSources =
            [ l1Dir / "Class1.cs"
              l1Dir / "obj/Debug/netstandard2.0/l1.AssemblyInfo.cs" ]
            |> List.map Path.GetFullPath

        // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
        // Expect.equal l1Parsed.SourceFiles l1ExpectedSources "check sources"

        let l2Parsed = parsed |> expectFind l2 "the F# lib"

        let l2ExpectedSources =
            [ l2Dir / "obj/Debug/netstandard2.0/l2.AssemblyInfo.fs"
              l2Dir / "Library.fs" ]
            |> List.map Path.GetFullPath


        let c1Parsed = parsed |> expectFind projPath "the F# console"


        let c1ExpectedSources =
            [ projDir / "obj/Debug/netcoreapp2.1/c1.AssemblyInfo.fs"
              projDir / "Program.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal parsed.Length 3 (sprintf "console (F#) and lib (F#) and lib (C#), but was %A" (parsed |> List.map (fun x -> x.ProjectFileName)))
        Expect.equal c1Parsed.SourceFiles c1ExpectedSources "check sources - C1"
        Expect.equal l1Parsed.SourceFiles l1ExpectedSources "check sources - L1"
        Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources - L2"

        Expect.equal l1Parsed l1Loaded "l1 notificaton and parsed should be the same"
        Expect.equal l2Parsed l2Loaded "l2 notificaton and parsed should be the same"
        Expect.equal c1Parsed c1Loaded "c1 notificaton and parsed should be the same")

let testSample4 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample4 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample4"
        copyDirFromAssets fs ``sample4 NetSdk multi tfm``.ProjDir testDir

        let projPath = testDir / (``sample4 NetSdk multi tfm``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        [ loading "m1.fsproj"
          loaded "m1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let [ _; WorkspaceProjectState.Loaded (m1Loaded, _, _) ] = watcher.Notifications


        Expect.equal parsed.Length 1 (sprintf "multi-tfm lib (F#), but was %A" (parsed |> List.map (fun x -> x.ProjectFileName)))

        let m1Parsed = parsed |> expectFind projPath "the F# console"

        let m1ExpectedSources =
            [ projDir / "obj/Debug/netstandard2.0/m1.AssemblyInfo.fs"
              projDir / "LibraryA.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal m1Parsed.SourceFiles m1ExpectedSources "check sources"
        Expect.equal m1Parsed m1Loaded "m1 notificaton and parsed should be the same")

let testSample5 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample5 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample5"
        copyDirFromAssets fs ``sample5 NetSdk CSharp library``.ProjDir testDir

        let projPath = testDir / (``sample5 NetSdk CSharp library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        [ loading "l2.csproj"
          loaded "l2.csproj" ]
        |> expectNotifications (watcher.Notifications)

        let [ _; WorkspaceProjectState.Loaded (l2Loaded, _, _) ] = watcher.Notifications


        Expect.equal parsed.Length 1 "lib"

        let l2Parsed = parsed |> expectFind projPath "a C# lib"

        let l2ExpectedSources =
            [ projDir / "Class1.cs"
              projDir / "obj/Debug/netstandard2.0/l2.AssemblyInfo.cs" ]
            |> List.map Path.GetFullPath

        // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
        // Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources"
        Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources"

        Expect.equal l2Parsed l2Loaded "l2 notificaton and parsed should be the same")

let testLoadSln toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) expected =
    testCase
    |> withLog (sprintf "can load sln - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sln"
        copyDirFromAssets fs ``sample6 Netsdk Sparse/sln``.ProjDir testDir

        let slnPath = testDir / (``sample6 Netsdk Sparse/sln``.ProjectFile)

        dotnet fs [ "restore"; slnPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadSln(slnPath) |> Seq.toList


        expected |> expectNotifications (watcher.Notifications)


        Expect.equal parsed.Length 3 "c1, l1, l2"

        let c1 = testDir / (``sample6 Netsdk Sparse/1``.ProjectFile)
        let c1Dir = Path.GetDirectoryName c1

        let [ l2 ] = ``sample6 Netsdk Sparse/1``.ProjectReferences |> List.map (fun p2p -> testDir / p2p.ProjectFile)
        let l2Dir = Path.GetDirectoryName l2

        let l1 = testDir / (``sample6 Netsdk Sparse/2``.ProjectFile)
        let l1Dir = Path.GetDirectoryName l1

        let l1Parsed = parsed |> expectFind l1 "the F# lib"

        let l1ExpectedSources =
            [ l1Dir / "obj/Debug/netstandard2.0/l1.AssemblyInfo.fs"
              l1Dir / "Library.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal l1Parsed.SourceFiles l1ExpectedSources "check sources l1"
        Expect.equal l1Parsed.ReferencedProjects [] "check p2p l1"

        let l2Parsed = parsed |> expectFind l2 "the C# lib"

        let l2ExpectedSources =
            [ l2Dir / "obj/Debug/netstandard2.0/l2.AssemblyInfo.fs"
              l2Dir / "Library.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources l2"
        Expect.equal l2Parsed.ReferencedProjects [] "check p2p l2"

        let c1Parsed = parsed |> expectFind c1 "the F# console"

        let c1ExpectedSources =
            [ c1Dir / "obj/Debug/netcoreapp2.1/c1.AssemblyInfo.fs"
              c1Dir / "Program.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal c1Parsed.SourceFiles c1ExpectedSources "check sources c1"
        Expect.equal c1Parsed.ReferencedProjects.Length 1 "check p2p c1"

    )

let testParseSln toolsPath =
    testCase
    |> withLog "can parse sln" (fun logger fs ->
        let testDir = inDir fs "parse_sln"
        copyDirFromAssets fs ``sample6 Netsdk Sparse/sln``.ProjDir testDir

        let slnPath = testDir / (``sample6 Netsdk Sparse/sln``.ProjectFile)

        dotnet fs [ "restore"; slnPath ] |> checkExitCodeZero

        let p = InspectSln.tryParseSln (slnPath)

        Expect.isTrue
            (match p with
             | Ok _ -> true
             | Result.Error _ -> false)
            "expected successful parse"

        let actualProjects =
            InspectSln.loadingBuildOrder (
                match p with
                | Ok (_, data) -> data
                | _ -> failwith "unreachable"
            )
            |> List.map Path.GetFullPath

        let expectedProjects =
            [ Path.Combine(testDir, "c1", "c1.fsproj")
              Path.Combine(testDir, "l1", "l1.fsproj")
              Path.Combine(testDir, "l2", "l2.fsproj") ]
            |> List.map Path.GetFullPath

        Expect.equal actualProjects expectedProjects "expected successful calculation of loading build order of solution"

    )

let testSample9 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample9 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample9"
        copyDirFromAssets fs ``sample9 NetSdk library``.ProjDir testDir
        // fs.cp (``sample9 NetSdk library``.ProjDir/"Directory.Build.props") testDir

        let projPath = testDir / (``sample9 NetSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        [ loading "n1.fsproj"
          loaded "n1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let [ _; WorkspaceProjectState.Loaded (n1Loaded, _, _) ] = watcher.Notifications


        Expect.equal parsed.Length 1 "console and lib"

        let n1Parsed = parsed |> expectFind projPath "first is a lib"

        let expectedSources =
            [ projDir / "obj2/Debug/netstandard2.0/n1.AssemblyInfo.fs"
              projDir / "Library.fs" ]
            |> List.map Path.GetFullPath

        Expect.equal n1Parsed.SourceFiles expectedSources "check sources"

        Expect.equal n1Parsed n1Loaded "notificaton and parsed should be the same")

let testRender2 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can render sample2 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "render_sample2"
        let sampleProj = ``sample2 NetSdk library``
        copyDirFromAssets fs sampleProj.ProjDir testDir

        let projPath = testDir / (sampleProj.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


        let n1Parsed = parsed |> expectFind projPath "first is a lib"

        let rendered = ProjectViewer.render n1Parsed

        let expectedSources = [ projDir / "Library.fs", "Library.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal rendered (renderOf sampleProj expectedSources) "check rendered project")

let testRender3 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can render sample3 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "render_sample3"
        let c1Proj = ``sample3 Netsdk projs``
        copyDirFromAssets fs c1Proj.ProjDir testDir

        let projPath = testDir / (c1Proj.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        let [ (l1Proj, l1, l1Dir); (l2Proj, l2, l2Dir) ] =
            c1Proj.ProjectReferences
            |> List.map (fun p2p -> p2p, Path.GetFullPath(testDir / p2p.ProjectFile))
            |> List.map (fun (p2p, path) -> p2p, path, Path.GetDirectoryName(path))

        dotnet fs [ "build"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        let l1Parsed = parsed |> expectFind l1 "the C# lib"

        let l2Parsed = parsed |> expectFind l2 "the F# lib"

        let c1Parsed = parsed |> expectFind projPath "the F# console"


        let l1ExpectedSources =
            [ l1Dir / "Class1.cs", "Class1.cs"
              l1Dir / "obj/Debug/netstandard2.0/l1.AssemblyInfo.cs", "obj/Debug/netstandard2.0/l1.AssemblyInfo.cs" ]
            |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal (ProjectViewer.render l1Parsed) (renderOf l1Proj l1ExpectedSources) "check rendered l1"

        let l2ExpectedSources = [ l2Dir / "Library.fs", "Library.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal (ProjectViewer.render l2Parsed) (renderOf l2Proj l2ExpectedSources) "check rendered l2"


        let c1ExpectedSources = [ projDir / "Program.fs", "Program.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal (ProjectViewer.render c1Parsed) (renderOf c1Proj c1ExpectedSources) "check rendered c1")

let testRender4 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can render sample4 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "render_sample4"
        let m1Proj = ``sample4 NetSdk multi tfm``
        copyDirFromAssets fs m1Proj.ProjDir testDir

        let projPath = testDir / (m1Proj.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath
        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        let m1Parsed = parsed |> expectFind projPath "the F# console"

        let m1ExpectedSources = [ projDir / "LibraryA.fs", "LibraryA.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal (ProjectViewer.render m1Parsed) (renderOf m1Proj m1ExpectedSources) "check rendered m1")

let testRender5 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can render sample5 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "render_sample5"
        let l2Proj = ``sample5 NetSdk CSharp library``
        copyDirFromAssets fs l2Proj.ProjDir testDir

        let projPath = testDir / (l2Proj.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


        let l2Parsed = parsed |> expectFind projPath "a C# lib"

        let l2ExpectedSources =
            [ projDir / "Class1.cs", "Class1.cs"
              projDir / "obj/Debug/netstandard2.0/l2.AssemblyInfo.cs", "obj/Debug/netstandard2.0/l2.AssemblyInfo.cs" ]
            |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
        Expect.equal (ProjectViewer.render l2Parsed) (renderOf l2Proj l2ExpectedSources) "check rendered l2")

let testRender8 toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can render sample8 - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "render_sample8"
        let sampleProj = ``sample8 NetSdk Explorer``
        copyDirFromAssets fs sampleProj.ProjDir testDir

        let projPath = testDir / (sampleProj.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


        let n1Parsed = parsed |> expectFind projPath "first is a lib"

        let rendered = ProjectViewer.render n1Parsed

        let expectedSources =
            [ projDir / "LibraryA.fs", "Component/TheLibraryA.fs"
              projDir / "LibraryC.fs", "LibraryC.fs"
              projDir / "LibraryB.fs", "Component/Auth/TheLibraryB.fs" ]
            |> List.map (fun (p, l) -> Path.GetFullPath p, l)

        Expect.equal rendered (renderOf sampleProj expectedSources) "check rendered project")

let testProjectNotFound toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "project not found - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "proj_not_found"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let wrongPath =
            let dir, name, ext = Path.GetDirectoryName projPath, Path.GetFileNameWithoutExtension projPath, Path.GetExtension projPath
            Path.Combine(dir, name + "aa" + ext)

        let parsed = loader.LoadProjects [ wrongPath ] |> Seq.toList


        [ ExpectNotification.loading "n1aa.fsproj"
          ExpectNotification.failed "n1aa.fsproj" ]
        |> expectNotifications (watcher.Notifications)


        Expect.equal parsed.Length 0 "no project loaded"

        Expect.equal (watcher.Notifications |> List.item 1) (WorkspaceProjectState.Failed(wrongPath, (GetProjectOptionsErrors.ProjectNotFound(wrongPath)))) "check error type"
    )

let internalGetProjectOptions =
    fun (r: FSharpReferencedProject) ->
        let rCase, fields =
            FSharp.Reflection.FSharpValue.GetUnionFields(r, typeof<FSharpReferencedProject>, System.Reflection.BindingFlags.NonPublic ||| System.Reflection.BindingFlags.Instance)

        if rCase.Name = "FSharpReference" then
            let projOptions: FSharpProjectOptions = rCase.GetFields().[1].GetValue(box r) :?> _
            Some projOptions
        else
            None

let testFCSmap toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample2 with FCS - %s" workspaceLoader) (fun logger fs ->

        let rec allFCSProjects (po: FSharpProjectOptions) =
            [ yield po
              for reference in po.ReferencedProjects do
                  match internalGetProjectOptions reference with
                  | Some opts -> yield! allFCSProjects opts
                  | None -> () ]


        let rec allP2P (po: FSharpProjectOptions) =
            [ for reference in po.ReferencedProjects do
                  let opts = internalGetProjectOptions reference |> Option.get
                  yield reference.FileName, opts
                  yield! allP2P opts ]

        let expectP2PKeyIsTargetPath (pos: Map<string, ProjectOptions>) fcsPo =
            for (tar, fcsPO) in allP2P fcsPo do
                let dpoPo = pos |> Map.find fcsPo.ProjectFileName
                Expect.equal tar dpoPo.TargetPath (sprintf "p2p key is TargetPath, fsc projet options was '%A'" fcsPO)

        let testDir = inDir fs "load_sample_fsc"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList
        let mutable pos = Map.empty

        loader.Notifications.Add (function | WorkspaceProjectState.Loaded (po, knownProjects, _) -> pos <- Map.add po.ProjectFileName po pos)

        let fcsPo = FCS.mapToFSharpProjectOptions parsed.Head parsed

        let po = parsed |> expectFind projPath "first is a lib"

        Expect.equal fcsPo.LoadTime po.LoadTime "load time"

        Expect.equal fcsPo.ReferencedProjects.Length ``sample2 NetSdk library``.ProjectReferences.Length "refs"

        //TODO check fullpaths
        Expect.equal fcsPo.SourceFiles (po.SourceFiles |> Array.ofList) "check sources"

        expectP2PKeyIsTargetPath pos fcsPo

        let fcs = createFCS ()
        let result = fcs.ParseAndCheckProject(fcsPo) |> Async.RunSynchronously

        Expect.isEmpty result.Diagnostics (sprintf "no errors but was: %A" result.Diagnostics)

        let uses = result.GetAllUsesOfAllSymbols()

        Expect.isNonEmpty uses "all symbols usages"

        )

let testFCSmapManyProj toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample3 with FCS - %s" workspaceLoader) (fun logger fs ->

        let rec allFCSProjects (po: FSharpProjectOptions) =
            [ yield po
              for reference in po.ReferencedProjects do
                  match internalGetProjectOptions reference with
                  | Some opts -> yield! allFCSProjects opts
                  | None -> () ]


        let rec allP2P (po: FSharpProjectOptions) =
            [ for reference in po.ReferencedProjects do
                  let opts = internalGetProjectOptions reference |> Option.get
                  yield reference.FileName, opts
                  yield! allP2P opts ]

        let expectP2PKeyIsTargetPath (pos: Map<string, ProjectOptions>) fcsPo =
            for (tar, fcsPO) in allP2P fcsPo do
                let dpoPo = pos |> Map.find fcsPo.ProjectFileName
                Expect.equal tar dpoPo.TargetPath (sprintf "p2p key is TargetPath, fsc projet options was '%A'" fcsPO)

        let testDir = inDir fs "load_sample_fsc"
        copyDirFromAssets fs  ``sample3 Netsdk projs``.ProjDir testDir

        let projPath = testDir / (``sample3 Netsdk projs``.ProjectFile)

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList
        let mutable pos = Map.empty

        loader.Notifications.Add (function | WorkspaceProjectState.Loaded (po, knownProjects, _) -> pos <- Map.add po.ProjectFileName po pos)

        let fcsPo = FCS.mapToFSharpProjectOptions parsed.Head parsed
        let hasCSharpRef = fcsPo.OtherOptions |> Seq.exists (fun opt -> opt.StartsWith "-r:" && opt.EndsWith "l1.dll")
        let hasCSharpProjectRef = fcsPo.ReferencedProjects |> Seq.exists (fun ref -> ref.FileName.EndsWith "l1.dll")
        let hasFSharpRef = fcsPo.OtherOptions |> Seq.exists (fun opt -> opt.StartsWith "-r:" && opt.EndsWith "l2.dll")
        let hasFSharpProjectRef = fcsPo.ReferencedProjects |> Seq.exists (fun ref -> ref.FileName.EndsWith "l2.dll")
        Expect.equal hasCSharpRef true "Should have direct dll reference to C# reference"
        Expect.equal hasCSharpProjectRef false "Should NOT have project reference to C# reference"
        Expect.equal hasFSharpRef true "Should have direct dll reference to F# reference"
        Expect.equal hasFSharpProjectRef true "Should have project reference to F# reference"

        )

let testSample2WithBinLog toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    testCase
    |> withLog (sprintf "can load sample2 with bin log - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample2_bin_log"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)
        let projDir = Path.GetDirectoryName projPath

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        let loader = workspaceFactory toolsPath

        let watcher = watchNotifications logger loader

        let parsed = loader.LoadProjects([ projPath ], [], BinaryLogGeneration.Within(DirectoryInfo projDir)) |> Seq.toList

        [ loading "n1.fsproj"
          loaded "n1.fsproj" ]
        |> expectNotifications (watcher.Notifications)

        let [ _; WorkspaceProjectState.Loaded (n1Loaded, _, _) ] = watcher.Notifications

        let n1Parsed = parsed |> expectFind projPath "first is a lib"

        let expectedSources =
            [ projDir / "obj/Debug/netstandard2.0/n1.AssemblyInfo.fs"
              projDir / "Library.fs" ]
            |> List.map Path.GetFullPath

        let blPath = projDir / "n1.binlog"
        let blExists = File.Exists blPath

        Expect.isTrue blExists "binlog file should exist"
        Expect.equal parsed.Length 1 "console and lib"
        Expect.equal n1Parsed n1Loaded "notificaton and parsed should be the same"
        Expect.equal n1Parsed.SourceFiles expectedSources "check sources")

[<AutoOpen>]
module ExpectProjectSystemNotification =

    open Ionide.ProjInfo.ProjectSystem

    let loading (name: string) =
        let isLoading n =
            match n with
            | ProjectResponse.ProjectLoading (path) when path.EndsWith(name) -> true
            | _ -> false

        sprintf "loading %s" name, isLoading

    let loaded (name: string) =
        let isLoaded n =
            match n with
            | ProjectResponse.Project (po, _) when po.ProjectFileName.EndsWith(name) -> true
            | _ -> false

        sprintf "loaded %s" name, isLoaded

    let failed (name: string) =
        let isFailed n =
            match n with
            | ProjectResponse.ProjectError (path, _) when path.EndsWith(name) -> true
            | _ -> false

        sprintf "failed %s" name, isFailed

    let workspace (status: bool) =
        let isFailed n =
            match n with
            | ProjectResponse.WorkspaceLoad (s) when s = status -> true
            | _ -> false

        sprintf "workspace %b" status, isFailed

    let changed (name: string) =
        let isFailed n =
            match n with
            | ProjectResponse.ProjectChanged (path) when path.EndsWith(name) -> true
            | _ -> false

        sprintf "changed %s" name, isFailed

    let expectNotifications actual expected =
        let getMessage =
            function
            | ProjectResponse.ProjectLoading (path) -> sprintf "loading %s" (System.IO.Path.GetFileName path)
            | ProjectResponse.Project (po, _) -> sprintf "loaded %s" (System.IO.Path.GetFileName po.ProjectFileName)
            | ProjectResponse.ProjectError (path, _) -> sprintf "failed %s" (System.IO.Path.GetFileName path)
            | ProjectResponse.WorkspaceLoad (finished) -> sprintf "workspace %b" finished
            | ProjectResponse.ProjectChanged (projectFileName) -> sprintf "changed %s" (System.IO.Path.GetFileName projectFileName)

        Expect.equal (List.length actual) (List.length expected) (sprintf "expected notifications: %A\n actual notifications %A" (expected |> List.map fst) (actual |> List.map getMessage))

        expected
        |> List.zip actual
        |> List.iter (fun (n, check) ->
            let name, f = check

            let minimal_info = getMessage n


            Expect.isTrue (f n) (sprintf "expected %s but was %s" name minimal_info))

    type NotificationWatcher(controller: ProjectController, log) =
        let notifications = List<_>()

        do
            controller.Notifications.Add (fun arg ->
                notifications.Add(arg)
                log arg)

        member __.Notifications = notifications |> List.ofSeq

    let logNotification (logger: Logger) arg =
        logger.debug (eventX "notified: {notification}'" >> setField "notification" arg)

    let watchNotifications logger controller =
        NotificationWatcher(controller, logNotification logger)

let testProjectSystem toolsPath workspaceLoader workspaceFactory =
    testCase
    |> withLog (sprintf "can load sample2 with Project System - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample2_projectSystem"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero


        use controller = new ProjectSystem.ProjectController(toolsPath, workspaceFactory)
        let watcher = watchNotifications logger controller
        controller.LoadProject(projPath)

        sleepABit ()

        let parsed = controller.ProjectOptions |> Seq.toList |> List.map (snd)
        let fcsPo = parsed.Head

        [ workspace false
          loading "n1.fsproj"
          loaded "n1.fsproj"
          workspace true ]
        |> expectNotifications (watcher.Notifications)


        Expect.equal fcsPo.ReferencedProjects.Length ``sample2 NetSdk library``.ProjectReferences.Length "refs"
        Expect.equal fcsPo.SourceFiles.Length 2 "files"

        let fcs = createFCS ()
        let result = fcs.ParseAndCheckProject(fcsPo) |> Async.RunSynchronously

        Expect.isEmpty result.Diagnostics (sprintf "no errors but was: %A" result.Diagnostics)

        let uses = result.GetAllUsesOfAllSymbols()

        Expect.isNonEmpty uses "all symbols usages"

    )

let testProjectSystemOnChange toolsPath workspaceLoader workspaceFactory =
    testCase
    |> withLog (sprintf "can load sample2 with Project System, detect change on fsproj - %s" workspaceLoader) (fun logger fs ->
        let testDir = inDir fs "load_sample2_projectSystem_onChange"
        copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

        let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

        dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

        use controller = new ProjectSystem.ProjectController(toolsPath, workspaceFactory)
        let watcher = watchNotifications logger controller
        controller.LoadProject(projPath)

        sleepABit ()


        [ workspace false
          loading "n1.fsproj"
          loaded "n1.fsproj"
          workspace true ]
        |> expectNotifications (watcher.Notifications)

        fs.touch projPath

        sleepABit ()

        [ workspace false
          loading "n1.fsproj"
          loaded "n1.fsproj"
          workspace true
          changed "n1.fsproj"
          workspace false
          loading "n1.fsproj"
          loaded "n1.fsproj"
          workspace true ]
        |> expectNotifications (watcher.Notifications)

    )

let debugTets toolsPath workspaceLoader (workspaceFactory: ToolsPath -> IWorkspaceLoader) =
    ptestCase
    |> withLog (sprintf "debug - %s" workspaceLoader) (fun logger fs ->

        let projPath = @"D:\Programowanie\Projekty\Ionide\dotnet-proj-info\src\Ionide.ProjInfo.Sln\Ionide.ProjInfo.Sln.csproj"

        let loader = workspaceFactory toolsPath

        let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

        printfn "%A" parsed

    )

let tests toolsPath =
    let testSample3WorkspaceLoaderExpected =
        [ ExpectNotification.loading "c1.fsproj"
          ExpectNotification.loading "l1.csproj"
          ExpectNotification.loaded "l1.csproj"
          ExpectNotification.loading "l2.fsproj"
          ExpectNotification.loaded "l2.fsproj"
          ExpectNotification.loaded "c1.fsproj" ]

    let testSample3GraphExpected =
        [ ExpectNotification.loading "c1.fsproj"
          ExpectNotification.loaded "c1.fsproj" ]

    let testSlnExpected =
        [ ExpectNotification.loading "c1.fsproj"
          ExpectNotification.loading "l2.fsproj"
          ExpectNotification.loaded "l2.fsproj"
          ExpectNotification.loaded "c1.fsproj"
          ExpectNotification.loading "l1.fsproj"
          ExpectNotification.loaded "l1.fsproj"
          ExpectNotification.loaded "l2.fsproj" ]

    let testSlnGraphExpected =
        [ ExpectNotification.loading "l2.fsproj"
          ExpectNotification.loading "l1.fsproj"
          ExpectNotification.loading "c1.fsproj"
          ExpectNotification.loaded "l2.fsproj"
          ExpectNotification.loaded "c1.fsproj"
          ExpectNotification.loaded "l1.fsproj" ]


    testSequenced
    <| testList
        "Main tests"
        [ testSample2 toolsPath "WorkspaceLoader" false (fun (tools, props) -> WorkspaceLoader.Create(tools, globalProperties = props))
          testSample2 toolsPath "WorkspaceLoader" true (fun (tools, props) -> WorkspaceLoader.Create(tools, globalProperties = props))
          testSample2 toolsPath "WorkspaceLoaderViaProjectGraph" false (fun (tools, props) -> WorkspaceLoaderViaProjectGraph.Create(tools, globalProperties = props))
          testSample2 toolsPath "WorkspaceLoaderViaProjectGraph" true (fun (tools, props) -> WorkspaceLoaderViaProjectGraph.Create(tools, globalProperties = props))
          testSample3 toolsPath "WorkspaceLoader" WorkspaceLoader.Create testSample3WorkspaceLoaderExpected //- Sample 3 having issues, was also marked pending on old test suite
          //   testSample3 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create testSample3GraphExpected //- Sample 3 having issues, was also marked pending on old test suite
          testSample4 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testSample4 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testSample5 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testSample5 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testSample9 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testSample9 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //Sln tests
          testLoadSln toolsPath "WorkspaceLoader" WorkspaceLoader.Create testSlnExpected
          //   testLoadSln toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create testSlnGraphExpected // Having issues on CI
          testParseSln toolsPath
          //Render tests
          testRender2 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testRender2 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testRender3 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          //   testRender3 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create //- Sample 3 having issues, was also marked pending on old test suite
          testRender4 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testRender4 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testRender5 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testRender5 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testRender8 toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testRender8 toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //Invalid tests
          testProjectNotFound toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testProjectNotFound toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //FCS tests
          testFCSmap toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testFCSmap toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //FCS multi-project tests
          testFCSmapManyProj toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testFCSmapManyProj toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //ProjectSystem tests
          testProjectSystem toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testProjectSystem toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          testProjectSystemOnChange toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testProjectSystemOnChange toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          debugTets toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          debugTets toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          //Binlog test
          testSample2WithBinLog toolsPath "WorkspaceLoader" WorkspaceLoader.Create
          testSample2WithBinLog toolsPath "WorkspaceLoaderViaProjectGraph" WorkspaceLoaderViaProjectGraph.Create
          test "can get runtimes" {
              let runtimes =
                  SdkDiscovery.runtimes (Paths.dotnetRoot.Value |> Option.defaultWith (fun _ -> failwith "unable to find dotnet binary"))

              Expect.isNonEmpty runtimes "should have found at least the currently-executing runtime"
          }
          test "can get sdks" {
              let sdks =
                  SdkDiscovery.sdks (Paths.dotnetRoot.Value |> Option.defaultWith (fun _ -> failwith "unable to find dotnet binary"))

              Expect.isNonEmpty sdks "should have found at least the currently-executing sdk"
          }
          testLegacyFrameworkProject toolsPath "can load legacy project file" false (fun (tools, props) -> WorkspaceLoader.Create(tools, globalProperties = props))
          testLegacyFrameworkMultiProject toolsPath "can load legacy multi project file" false (fun (tools, props) -> WorkspaceLoader.Create(tools, globalProperties = props)) ]
