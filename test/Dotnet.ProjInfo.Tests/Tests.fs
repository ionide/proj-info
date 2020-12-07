module Tests

open Expecto
open FileUtils
open Medallion.Shell
open System.IO
open Expecto.Logging
open DotnetProjInfo.TestAssets
open Dotnet.ProjInfo
open System.Collections.Generic
open Dotnet.ProjInfo.Types
open Dotnet.ProjInfo
open Expecto.Logging.Message
open FSharp.Compiler.SourceCodeServices

let RepoDir = (__SOURCE_DIRECTORY__ / ".." / "..") |> Path.GetFullPath
let ExamplesDir = RepoDir / "test" / "examples"
let TestRunDir = RepoDir / "test" / "testrun_ws"
let TestRunInvariantDir = TestRunDir / "invariant"

let checkExitCodeZero (cmd: Command) =
    Expect.equal 0 cmd.Result.ExitCode "command finished with exit code non-zero."

let findByPath path parsed =
    parsed
    |> Array.tryPick (fun (kv: KeyValuePair<string, ProjectOptions>) -> if kv.Key = path then Some kv else None)
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
    test
        name
        (fun () ->

            let logger = Log.create (sprintf "Test '%s'" name)
            let fs = FileUtils(logger)
            f logger fs)

let renderOf sampleProj sources =
    { ProjectViewerTree.Name = sampleProj.ProjectFile |> Path.GetFileNameWithoutExtension
      Items = sources |> List.map (fun (path, link) -> ProjectViewerItem.Compile(path, { ProjectViewerItemConfig.Link = link })) }

let createFCS () =

    let checker =
        FSharp.Compiler.SourceCodeServices.FSharpChecker.Create(projectCacheSize = 200, keepAllBackgroundResolutions = true, keepAssemblyContents = true)

    checker.ImplicitlyStartBackgroundWork <- true

    checker

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
        Expect.equal (List.length actual) (List.length expected) (sprintf "notifications: %A" (expected |> List.map fst))

        expected
        |> List.zip actual
        |> List.iter
            (fun (n, check) ->
                let name, f = check

                let minimal_info =
                    match n with
                    | WorkspaceProjectState.Loading (path) -> sprintf "loading %s " path
                    | WorkspaceProjectState.Loaded (po, _, _) -> sprintf "loaded %s" po.ProjectFileName
                    | WorkspaceProjectState.Failed (path, _) -> sprintf "failed %s" path

                Expect.isTrue (f n) (sprintf "expected %s but was %s" name minimal_info))

    type NotificationWatcher(loader: Dotnet.ProjInfo.WorkspaceLoader, log) =
        let notifications = List<_>()

        do
            loader.Notifications.Add
                (fun arg ->
                    notifications.Add(arg)
                    log arg)

        member __.Notifications = notifications |> List.ofSeq

    let logNotification (logger: Logger) arg =
        logger.debug (eventX "notified: {notification}'" >> setField "notification" arg)

    let watchNotifications logger loader =
        NotificationWatcher(loader, logNotification logger)

let testSample2 toolsPath =
    testCase
    |> withLog
        "can load sample2"
        (fun logger fs ->
            let testDir = inDir fs "load_sample2"
            copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

            let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            [ loading "n1.fsproj"
              loaded "n1.fsproj" ]
            |> expectNotifications (watcher.Notifications)

            let [ _; WorkspaceProjectState.Loaded (n1Loaded, _, _) ] = watcher.Notifications

            let n1Parsed = parsed |> expectFind projPath "first is a lib"

            let expectedSources =
                [ projDir / "obj/Debug/netstandard2.0/n1.AssemblyInfo.fs"
                  projDir / "Library.fs" ]
                |> List.map Path.GetFullPath

            Expect.equal parsed.Length 1 "console and lib"
            Expect.equal n1Parsed n1Loaded "notificaton and parsed should be the same"
            Expect.equal n1Parsed.SourceFiles expectedSources "check sources")

let testSample3 toolsPath =
    testCase
    |> withLog
        "can load sample3"
        (fun logger fs ->
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

            let loader = WorkspaceLoader.Create(toolsPath)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            [ loading "c1.fsproj"
              loading "l1.csproj"
              loading "l2.fsproj"
              loaded "c1.fsproj"
              loaded "l1.csproj"
              loaded "l2.fsproj" ]
            |> expectNotifications (watcher.Notifications)

            let [ _; _; _; WorkspaceProjectState.Loaded (c1Loaded, _, _); WorkspaceProjectState.Loaded (l1Loaded, _, _); WorkspaceProjectState.Loaded (l2Loaded, _, _) ] = watcher.Notifications



            let l1Parsed = parsed |> expectFind l1 "the C# lib"

            let l1ExpectedSources =
                [ l1Dir / "obj/Debug/netstandard2.0/l1.AssemblyInfo.cs"
                  l1Dir / "Class1.cs" ]
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
            Expect.equal l1Parsed.SourceFiles [] "check sources - L1"
            Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources - L2"

            Expect.equal l1Parsed l1Loaded "l1 notificaton and parsed should be the same"
            Expect.equal l2Parsed l2Loaded "l2 notificaton and parsed should be the same"
            Expect.equal c1Parsed c1Loaded "c1 notificaton and parsed should be the same")

let testSample4 toolsPath =
    testCase
    |> withLog
        "can load sample4"
        (fun logger fs ->
            let testDir = inDir fs "load_sample4"
            copyDirFromAssets fs ``sample4 NetSdk multi tfm``.ProjDir testDir

            let projPath = testDir / (``sample4 NetSdk multi tfm``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

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

let testSample5 toolsPath =
    testCase
    |> withLog
        "can load sample5"
        (fun logger fs ->
            let testDir = inDir fs "load_sample5"
            copyDirFromAssets fs ``sample5 NetSdk CSharp library``.ProjDir testDir

            let projPath = testDir / (``sample5 NetSdk CSharp library``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            [ loading "l2.csproj"
              loaded "l2.csproj" ]
            |> expectNotifications (watcher.Notifications)

            let [ _; WorkspaceProjectState.Loaded (l2Loaded, _, _) ] = watcher.Notifications


            Expect.equal parsed.Length 1 "lib"

            let l2Parsed = parsed |> expectFind projPath "a C# lib"

            let _l2ExpectedSources =
                [ projDir / "obj/Debug/netstandard2.0/l2.AssemblyInfo.cs"
                  projDir / "Class1.cs" ]
                |> List.map Path.GetFullPath

            // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
            // Expect.equal l2Parsed.SourceFiles l2ExpectedSources "check sources"
            Expect.equal l2Parsed.SourceFiles [] "check sources"

            Expect.equal l2Parsed l2Loaded "l2 notificaton and parsed should be the same")

let testLoadSln toolsPath =
    testCase
    |> withLog
        "can load sln"
        (fun logger fs ->
            let testDir = inDir fs "load_sln"
            copyDirFromAssets fs ``sample6 Netsdk Sparse/sln``.ProjDir testDir

            let slnPath = testDir / (``sample6 Netsdk Sparse/sln``.ProjectFile)

            dotnet fs [ "restore"; slnPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let watcher = watchNotifications logger loader

            let parsed = loader.LoadSln(slnPath) |> Seq.toList

            for w in watcher.Notifications |> Seq.map (fun n -> n.DebugPrint) do
                printfn "%s" w

            [ loading "c1.fsproj"
              loading "l2.fsproj"
              loaded "l2.fsproj"
              loaded "c1.fsproj"
              loading "l1.fsproj"
              loaded "l1.fsproj"
              loaded "l2.fsproj" ]
            |> expectNotifications (watcher.Notifications)


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
    |> withLog
        "can parse sln"
        (fun logger fs ->
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
                InspectSln.loadingBuildOrder
                    (match p with
                     | Ok (_, data) -> data
                     | _ -> failwith "unreachable")
                |> List.map Path.GetFullPath

            let expectedProjects =
                [ Path.Combine(testDir, "c1", "c1.fsproj")
                  Path.Combine(testDir, "l1", "l1.fsproj")
                  Path.Combine(testDir, "l2", "l2.fsproj") ]
                |> List.map Path.GetFullPath

            Expect.equal actualProjects expectedProjects "expected successful calculation of loading build order of solution"

            )

let testSample9 toolsPath =
    testCase
    |> withLog
        "can load sample9"
        (fun logger fs ->
            let testDir = inDir fs "load_sample9"
            copyDirFromAssets fs ``sample9 NetSdk library``.ProjDir testDir
            // fs.cp (``sample9 NetSdk library``.ProjDir/"Directory.Build.props") testDir

            let projPath = testDir / (``sample9 NetSdk library``.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

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

let testRender2 toolsPath =
    testCase
    |> withLog
        "can render sample2"
        (fun logger fs ->
            let testDir = inDir fs "render_sample2"
            let sampleProj = ``sample2 NetSdk library``
            copyDirFromAssets fs sampleProj.ProjDir testDir

            let projPath = testDir / (sampleProj.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


            let n1Parsed = parsed |> expectFind projPath "first is a lib"

            let rendered = ProjectViewer.render n1Parsed

            let expectedSources = [ projDir / "Library.fs", "Library.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            Expect.equal rendered (renderOf sampleProj expectedSources) "check rendered project")

let testRender3 toolsPath =
    testCase
    |> withLog
        "can render sample3"
        (fun logger fs ->
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

            let loader = WorkspaceLoader.Create(toolsPath)

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            let l1Parsed = parsed |> expectFind l1 "the C# lib"

            let l2Parsed = parsed |> expectFind l2 "the F# lib"

            let c1Parsed = parsed |> expectFind projPath "the F# console"


            let _l1ExpectedSources = [ l1Dir / "Class1.fs", "Class1.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
            Expect.equal (ProjectViewer.render l1Parsed) (renderOf l1Proj []) "check rendered l1"

            let l2ExpectedSources = [ l2Dir / "Library.fs", "Library.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            Expect.equal (ProjectViewer.render l2Parsed) (renderOf l2Proj l2ExpectedSources) "check rendered l2"


            let c1ExpectedSources = [ projDir / "Program.fs", "Program.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            Expect.equal (ProjectViewer.render c1Parsed) (renderOf c1Proj c1ExpectedSources) "check rendered c1")

let testRender4 toolsPath =
    testCase
    |> withLog
        "can render sample4"
        (fun logger fs ->
            let testDir = inDir fs "render_sample4"
            let m1Proj = ``sample4 NetSdk multi tfm``
            copyDirFromAssets fs m1Proj.ProjDir testDir

            let projPath = testDir / (m1Proj.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)
            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            let m1Parsed = parsed |> expectFind projPath "the F# console"

            let m1ExpectedSources = [ projDir / "LibraryA.fs", "LibraryA.fs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            Expect.equal (ProjectViewer.render m1Parsed) (renderOf m1Proj m1ExpectedSources) "check rendered m1")

let testRender5 toolsPath =
    testCase
    |> withLog
        "can render sample5"
        (fun logger fs ->
            let testDir = inDir fs "render_sample5"
            let l2Proj = ``sample5 NetSdk CSharp library``
            copyDirFromAssets fs l2Proj.ProjDir testDir

            let projPath = testDir / (l2Proj.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


            let l2Parsed = parsed |> expectFind projPath "a C# lib"

            let l2ExpectedSources = [ projDir / "Class1.cs", "Class1.cs" ] |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            // TODO C# doesnt have OtherOptions or SourceFiles atm. it should
            Expect.equal (ProjectViewer.render l2Parsed) (renderOf l2Proj []) "check rendered l2")

let testRender8 toolsPath =
    testCase
    |> withLog
        "can render sample8"
        (fun logger fs ->
            let testDir = inDir fs "render_sample8"
            let sampleProj = ``sample8 NetSdk Explorer``
            copyDirFromAssets fs sampleProj.ProjDir testDir

            let projPath = testDir / (sampleProj.ProjectFile)
            let projDir = Path.GetDirectoryName projPath

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList


            let n1Parsed = parsed |> expectFind projPath "first is a lib"

            let rendered = ProjectViewer.render n1Parsed

            let expectedSources =
                [ projDir / "LibraryA.fs", "Component/TheLibraryA.fs"
                  projDir / "LibraryC.fs", "LibraryC.fs"
                  projDir / "LibraryB.fs", "Component/Auth/TheLibraryB.fs" ]
                |> List.map (fun (p, l) -> Path.GetFullPath p, l)

            Expect.equal rendered (renderOf sampleProj expectedSources) "check rendered project")

let testProjectNotFound toolsPath =
    testCase
    |> withLog
        "project not found"
        (fun logger fs ->
            let testDir = inDir fs "proj_not_found"
            copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

            let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let loader = WorkspaceLoader.Create(toolsPath)

            let watcher = watchNotifications logger loader

            let wrongPath =
                let dir, name, ext = Path.GetDirectoryName projPath, Path.GetFileNameWithoutExtension projPath, Path.GetExtension projPath
                Path.Combine(dir, name + "aa" + ext)

            let parsed = loader.LoadProjects [ wrongPath ] |> Seq.toList

            [ loading "n1aa.fsproj"
              failed "n1aa.fsproj" ]
            |> expectNotifications (watcher.Notifications)


            Expect.equal parsed.Length 0 "no project loaded"

            Expect.equal (watcher.Notifications |> List.item 1) (WorkspaceProjectState.Failed(wrongPath, (GetProjectOptionsErrors.ProjectNotFound(wrongPath)))) "check error type")

let testFCSmap toolsPath =
    testCase
    |> withLog
        "can load sample2 with FCS"
        (fun logger fs ->
            let rec allFCSProjects (po: FSharpProjectOptions) =
                [ yield po
                  for (_, p2p) in po.ReferencedProjects do
                      yield! allFCSProjects p2p ]

            let findProjectExtraInfo (po: FSharpProjectOptions) =
                match po.ExtraProjectInfo with
                | None -> failwithf "expect ExtraProjectInfo but was None"
                | Some extra ->
                    match extra with
                    | :? ProjectOptions as poDPW -> poDPW
                    | ex -> failwithf "expected ProjectOptions but was '%A'" ex

            let rec allP2P (po: FSharpProjectOptions) =
                [ for (key, p2p) in po.ReferencedProjects do
                    let poDPW = findProjectExtraInfo p2p
                    yield (key, p2p, poDPW)
                    yield! allP2P p2p ]

            let expectP2PKeyIsTargetPath po =
                for (tar, fcsPO, poDPW) in allP2P po do
                    Expect.equal tar poDPW.TargetPath (sprintf "p2p key is TargetPath, fsc projet options was '%A'" fcsPO)



            let testDir = inDir fs "load_sample_fsc"
            copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

            let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero


            let loader = WorkspaceLoader.Create(toolsPath)

            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            let fcsPo = FCS.mapToFSharpProjectOptions parsed.Head parsed

            let po = parsed |> expectFind projPath "first is a lib"

            Expect.equal fcsPo.LoadTime po.LoadTime "load time"

            Expect.equal fcsPo.ReferencedProjects.Length ``sample2 NetSdk library``.ProjectReferences.Length "refs"

            Expect.equal fcsPo.ExtraProjectInfo (Some(box po)) "extra info"

            //TODO check fullpaths
            Expect.equal fcsPo.SourceFiles (po.SourceFiles |> Array.ofList) "check sources"


            expectP2PKeyIsTargetPath fcsPo

            let fcs = createFCS ()
            let result = fcs.ParseAndCheckProject(fcsPo) |> Async.RunSynchronously

            Expect.isEmpty result.Errors (sprintf "no errors but was: %A" result.Errors)

            let uses = result.GetAllUsesOfAllSymbols() |> Async.RunSynchronously

            Expect.isNonEmpty uses "all symbols usages"

            )

[<AutoOpen>]
module ExpectProjectSystemNotification =

    open Dotnet.ProjInfo.ProjectSystem

    let loading (name: string) =
        let isLoading n =
            match n with
            | ProjectResponse.ProjectLoading (path) when path.EndsWith(name) -> true
            | _ -> false

        sprintf "loading %s" name, isLoading

    let loaded (name: string) =
        let isLoaded n =
            match n with
            | ProjectResponse.Project (po) when po.ProjectFileName.EndsWith(name) -> true
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
        Expect.equal (List.length actual) (List.length expected) (sprintf "notifications: %A" (expected |> List.map fst))

        expected
        |> List.zip actual
        |> List.iter
            (fun (n, check) ->
                let name, f = check

                let minimal_info =
                    match n with
                    | ProjectResponse.ProjectLoading (path) -> sprintf "loading %s" path
                    | ProjectResponse.Project (po) -> sprintf "loaded %s" po.ProjectFileName
                    | ProjectResponse.ProjectError (path, _) -> sprintf "failed %s" path
                    | ProjectResponse.WorkspaceLoad (finished) -> sprintf "workspace %b" finished
                    | ProjectResponse.ProjectChanged (projectFileName) -> sprintf "changed %s" projectFileName

                Expect.isTrue (f n) (sprintf "expected %s but was %s" name minimal_info))

    type NotificationWatcher(controller: ProjectController, log) =
        let notifications = List<_>()

        do
            controller.Notifications.Add
                (fun arg ->
                    notifications.Add(arg)
                    log arg)

        member __.Notifications = notifications |> List.ofSeq

    let logNotification (logger: Logger) arg =
        logger.debug (eventX "notified: {notification}'" >> setField "notification" arg)

    let watchNotifications logger controller =
        NotificationWatcher(controller, logNotification logger)

let testProjectSystem toolsPath =
    testCase
    |> withLog
        "can load sample2 with Project System"
        (fun logger fs ->
            let testDir = inDir fs "load_sample2_projectSystem"
            copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

            let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let fcs = createFCS ()
            let controller = ProjectSystem.ProjectController(fcs, toolsPath)
            let watcher = watchNotifications logger controller
            controller.LoadProject(projPath)

            System.Threading.Thread.Sleep 1000

            let parsed = controller.ProjectOptions |> Seq.toList |> List.map (snd)
            let fcsPo = parsed.Head

            [ workspace false
              loading "n1.fsproj"
              loaded "n1.fsproj"
              workspace true ]
            |> expectNotifications (watcher.Notifications)


            Expect.equal fcsPo.ReferencedProjects.Length ``sample2 NetSdk library``.ProjectReferences.Length "refs"
            Expect.equal fcsPo.SourceFiles.Length 2 "files"

            let result = fcs.ParseAndCheckProject(fcsPo) |> Async.RunSynchronously

            Expect.isEmpty result.Errors (sprintf "no errors but was: %A" result.Errors)

            let uses = result.GetAllUsesOfAllSymbols() |> Async.RunSynchronously

            Expect.isNonEmpty uses "all symbols usages"

            )

let testProjectSystemOnChange toolsPath =
    testCase
    |> withLog
        "can load sample2 with Project System, detect change on fsproj"
        (fun logger fs ->
            let testDir = inDir fs "load_sample2_projectSystem_onChange"
            copyDirFromAssets fs ``sample2 NetSdk library``.ProjDir testDir

            let projPath = testDir / (``sample2 NetSdk library``.ProjectFile)

            dotnet fs [ "restore"; projPath ] |> checkExitCodeZero

            let fcs = createFCS ()
            let controller = ProjectSystem.ProjectController(fcs, toolsPath)
            let watcher = watchNotifications logger controller
            controller.LoadProject(projPath)

            System.Threading.Thread.Sleep 1000


            [ workspace false
              loading "n1.fsproj"
              loaded "n1.fsproj"
              workspace true ]
            |> expectNotifications (watcher.Notifications)

            fs.touch projPath

            System.Threading.Thread.Sleep 1000

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

let tests toolsPath =
    testSequenced
    <| testList
        "Main tests"
        [ testSample2 toolsPath
          //testSample3 toolsPath - Sample 3 having issues, was also marked pending on old test suite
          testSample4 toolsPath
          testSample5 toolsPath
          testSample9 toolsPath
          //Sln tests
          testLoadSln toolsPath
          testParseSln toolsPath
          //Render tests
          testRender2 toolsPath
          // testRender3 toolsPath - Sample 3 having issues, was also marked pending on old test suite
          testRender4 toolsPath
          testRender5 toolsPath
          testRender8 toolsPath
          //Invalid tests
          testProjectNotFound toolsPath
          //FCS tests
          testFCSmap toolsPath
          //ProjectSystem tests
          testProjectSystem toolsPath
          testProjectSystemOnChange toolsPath ]
