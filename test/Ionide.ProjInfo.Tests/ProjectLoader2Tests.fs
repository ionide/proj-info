namespace Ionide.ProjInfo.Tests

module ProjectLoader2Tests =

    open System
    open System.IO
    open System.Diagnostics
    open System.Threading
    open System.Threading.Tasks
    open Expecto
    open Ionide.ProjInfo
    open Ionide.ProjInfo.Types
    open FileUtils
    open Ionide.ProjInfo.Tests.TestUtils
    open DotnetProjInfo.TestAssets
    open Ionide.ProjInfo.Logging
    open Expecto.Logging
    open Ionide.ProjInfo.ProjectLoader
    open Microsoft.Build.Graph
    open Microsoft.Build.Evaluation
    open Microsoft.Build.Framework
    open Microsoft.Build.Execution
    open System.Linq


    type Binlogs(binlog: FileInfo) =
        let sw = new StringWriter()
        let errorLogger = new ErrorLogger()

        let loggers name =
            ProjectLoader.createLoggers name (BinaryLogGeneration.Within(binlog.Directory)) sw (Some errorLogger)

        member x.ErrorLogger = errorLogger
        member x.Loggers name = loggers name

        member x.Directory = binlog.Directory
        member x.File = binlog

        interface IDisposable with
            member this.Dispose() = sw.Dispose()


    type TestEnv = {
        Logger: Logger
        FS: FileUtils
        Binlog: Binlogs
        Data: TestAssetProjInfo2
        Entrypoints: string seq
        TestDir: DirectoryInfo
    } with

        interface IDisposable with
            member this.Dispose() = (this.Binlog :> IDisposable).Dispose()


    let projectCollection () =
        new ProjectCollection(
            globalProperties = dict ProjectLoader.defaultGlobalProps,
            loggers = null,
            remoteLoggers = null,
            toolsetDefinitionLocations = ToolsetDefinitionLocations.Local,
            maxNodeCount = Environment.ProcessorCount,
            onlyLogCriticalEvents = false,
            loadProjectsReadOnly = true
        )

    type IWorkspaceLoader2 =
        abstract member Load: paths: string list * ct: CancellationToken -> Task<seq<Result<BuildResult, BuildResult * ErrorLogger>>>


    let parseWithGraph (env: TestEnv) =
        task {
            let entrypoints =
                env.Entrypoints
                |> Seq.map ProjectGraphEntryPoint

            let loggers = env.Binlog.Loggers env.Binlog.File.Name

            // Evaluation
            use pc = projectCollection ()
            let graph = ProjectLoader2.EvaluateAsGraphAllTfms(entrypoints, pc)

            // Execution
            let bp = BuildParameters(Loggers = loggers)
            let bm = new BuildManagerSession()

            let! (result: Result<GraphBuildResult, BuildErrors<GraphBuildResult>>) = ProjectLoader2.Execution(bm, graph, bp)


            // Parse
            let projectsAfterBuild =
                match result with
                | Ok result ->
                    ProjectLoader2.Parse result
                    |> Seq.choose (
                        function
                        | Ok(LoadedProjectInfo.StandardProjectInfo x) -> Some x
                        | _ -> None
                    )
                | Result.Error(BuildErrors.BuildErr(result, errorLogs)) ->

                    Seq.empty

            return result, projectsAfterBuild
        }

    let parseWithProjectWalker (env: TestEnv) =
        task {
            let path = env.Entrypoints

            let entrypoints =
                path
                |> Seq.collect (fun p ->
                    if p.EndsWith(".sln") then
                        p
                        |> InspectSln.tryParseSln
                        |> getResult
                        |> InspectSln.loadingBuildOrder
                    else
                        [ p ]
                )
            // Evaluation
            use pc = projectCollection ()

            let allprojects =
                ProjectLoader2.EvaluateAsProjectsAllTfms(entrypoints, projectCollection = pc)
                |> Seq.toList

            let createBuildParametersFromProject (p: Project) =
                let fi = FileInfo p.FullPath
                let projectName = Path.GetFileNameWithoutExtension fi.Name

                let tfm =
                    match p.GlobalProperties.TryGetValue("TargetFramework") with
                    | true, tfm -> tfm.Replace('.', '_')
                    | _ -> ""

                let normalized = $"{projectName}-{tfm}"
                Some(BuildParameters(Loggers = env.Binlog.Loggers normalized))

            // Execution
            let bm = new BuildManagerSession()
            let! (results: Result<BuildResult, BuildErrors<BuildResult>> array) = ProjectLoader2.ExecutionWalkReferences(bm, allprojects, createBuildParametersFromProject)

            let projectsAfterBuild =
                results
                |> Seq.choose (
                    function
                    | Ok result ->
                        match ProjectLoader2.Parse result with
                        | Ok(LoadedProjectInfo.StandardProjectInfo x) -> Some x
                        | _ -> None
                    | _ -> None
                )

            return results, projectsAfterBuild
        }

    let testWithEnv name (data: TestAssetProjInfo2) f test =
        test
            name
            (fun () ->
                task {
                    let logger = Log.create (sprintf "Test '%s'" name)
                    let fs = FileUtils logger

                    let testDir = inDir fs name
                    copyDirFromAssets fs data.ProjDir testDir

                    let entrypoints =
                        data.EntryPoints
                        |> Seq.map (fun x ->
                            testDir
                            / x
                        )

                    entrypoints
                    |> Seq.iter (fun x ->
                        dotnet fs [
                            "restore"
                            x
                        ]
                        |> checkExitCodeZero
                    )

                    let binlog = new FileInfo(Path.Combine(testDir, $"{name}.binlog"))
                    use blc = new Binlogs(binlog)

                    let env = {
                        Logger = logger
                        FS = fs
                        Binlog = blc
                        Data = data
                        Entrypoints = entrypoints
                        TestDir = DirectoryInfo testDir
                    }

                    try
                        do! f env
                    with e ->

                        logger.error (
                            Message.eventX "binlog path {binlog}"
                            >> Message.setField "binlog" binlog.FullName
                        )

                        Exception.reraiseAny e
                }
            )


    let applyTests name (info: TestAssetProjInfo2) = [
        testCaseTask
        |> testWithEnv
            $"Graph.{name}"
            info
            (fun env ->
                task {
                    let! result, projectsAfterBuild = parseWithGraph env

                    do! env.Data.ExpectsGraphResult result
                    do! env.Data.ExpectsProjectOptions projectsAfterBuild
                }
            )
        testCaseTask
        |> testWithEnv
            $"Project.{name}"
            info
            (fun env ->
                task {
                    let! result, projectsAfterBuild = parseWithProjectWalker env

                    do! env.Data.ExpectsProjectResult result
                    do! env.Data.ExpectsProjectOptions projectsAfterBuild
                }
            )
    ]


    let buildManagerSessionTests toolsPath =
        ftestList "buildManagerSessionTests" [
            yield! applyTests "loader2-no-solution-with-2-projects" ``loader2-no-solution-with-2-projects``

            yield! applyTests "sample2-NetSdk-library2" ``sample2-NetSdk-library2``
            yield! applyTests "sample3-Netsdk-projs" ``sample3-Netsdk-projs-2``

            yield! applyTests "sample4-NetSdk-multitfm" ``sample4-NetSdk-multitfm-2``
            yield! applyTests "sample5-NetSdk-lib-cs" ``sample5-NetSdk-lib-cs-2``
            yield! applyTests "sample6-NetSdk-sparse" ``sample6-Netsdk-Sparse-sln-2``
            yield! applyTests "sample7-oldsdk-projs" ``sample7-legacy-framework-multi-project-2``
            yield! applyTests "sample8-NetSdk-Explorer" ``sample8-NetSdk-Explorer-2``
            yield! applyTests "sample9-NetSdk-library" ``sample9-NetSdk-library-2``
            yield! applyTests "sample10-NetSdk-custom-targets" ``sample10-NetSdk-library-with-custom-targets-2``


            testCaseTask
            |> testWithEnv
                "sample2-NetSdk-library2 - Graph"
                ``sample2-NetSdk-library2``
                (fun env ->
                    task {
                        let projPath =
                            env.TestDir.FullName
                            / env.Data.EntryPoints.Single()

                        let projDir = Path.GetDirectoryName projPath

                        let path =
                            env.Entrypoints
                            |> Seq.map ProjectGraphEntryPoint

                        let loggers = env.Binlog.Loggers env.Binlog.File.Name

                        // Evaluation
                        use pc = projectCollection ()

                        let graph = ProjectLoader2.EvaluateAsGraphAllTfms(path, pc)

                        // Execution
                        let bp = BuildParameters(Loggers = loggers)
                        let bm = new BuildManagerSession()

                        let! (result: Result<GraphBuildResult, BuildErrors<GraphBuildResult>>) = ProjectLoader2.Execution(bm, graph, bp)

                        let expectedSources =
                            [
                                projDir
                                / "obj/Debug/netstandard2.0/n1.AssemblyInfo.fs"
                                projDir
                                / "obj/Debug/netstandard2.0/.NETStandard,Version=v2.0.AssemblyAttributes.fs"
                                projDir
                                / "Library.fs"
                            ]
                            |> List.map Path.GetFullPath

                        match result with
                        | Result.Error _ -> failwith "expected success"
                        | Ok result ->
                            ProjectLoader2.Parse result
                            |> Seq.choose (

                                function
                                | Ok(LoadedProjectInfo.StandardProjectInfo x) -> Some x
                                | _ -> None
                            )
                            |> Seq.iter (fun x -> Expect.equal x.SourceFiles expectedSources "")

                            ()

                    }
                )


            testCaseTask
            |> testWithEnv
                "sample2-NetSdk-library2"
                ``sample2-NetSdk-library2``
                (fun env ->
                    task {
                        let projPath =
                            env.TestDir.FullName
                            / env.Data.EntryPoints.Single()

                        let projDir = Path.GetDirectoryName projPath


                        let entryPoints = env.Entrypoints

                        let loggers = env.Binlog.Loggers

                        // Evaluation
                        use pc = projectCollection ()

                        let createBuildParametersFromProject (p: Project) =
                            let fi = FileInfo p.FullPath
                            let projectName = Path.GetFileNameWithoutExtension fi.Name

                            let tfm =
                                match p.GlobalProperties.TryGetValue("TargetFramework") with
                                | true, tfm -> tfm.Replace('.', '_')
                                | _ -> ""

                            let normalized = $"{projectName}-{tfm}"

                            Some(BuildParameters(Loggers = env.Binlog.Loggers normalized))

                        let projs = ProjectLoader2.EvaluateAsProjectsAllTfms(entryPoints, projectCollection = pc)

                        // Execution

                        let bm = new BuildManagerSession()

                        let! (results: Result<_, BuildErrors<BuildResult>> array) = ProjectLoader2.ExecutionWalkReferences(bm, projs, createBuildParametersFromProject)

                        let result =
                            results
                            |> Seq.head

                        let expectedSources =
                            [
                                projDir
                                / "obj/Debug/netstandard2.0/n1.AssemblyInfo.fs"
                                projDir
                                / "obj/Debug/netstandard2.0/.NETStandard,Version=v2.0.AssemblyAttributes.fs"
                                projDir
                                / "Library.fs"
                            ]
                            |> List.map Path.GetFullPath

                        match result with
                        | Result.Error _ -> failwith "expected success"
                        | Ok result ->
                            match ProjectLoader2.Parse result with


                            | Ok(LoadedProjectInfo.StandardProjectInfo x) -> Expect.equal x.SourceFiles expectedSources ""
                            | _ -> failwith "lol"

                    }
                )

            testCaseTask
            |> testWithEnv
                "Concurrency - don't crash on concurrent builds"
                ``loader2-concurrent``
                (fun env ->
                    task {
                        let path =
                            env.Entrypoints
                            |> Seq.map ProjectGraphEntryPoint

                        use pc = projectCollection ()

                        let bp = BuildParameters(Loggers = env.Binlog.Loggers env.Binlog.File.Name)

                        let bm = new BuildManagerSession()

                        let work: Async<Result<GraphBuildResult, BuildErrors<GraphBuildResult>>> =
                            async {

                                // Evaluation
                                let graph = ProjectLoader2.EvaluateAsGraph(path, pc)

                                // Execution
                                return!
                                    ProjectLoader2.Execution(bm, graph, buildParameters = bp)
                                    |> Async.AwaitTask
                            }

                        // Should be throttled so concurrent builds won't fail
                        let! _ =
                            Async.Parallel [
                                work
                                work
                                work
                            // work
                            ]


                        ()

                    }
                )

            testCaseTask
            |> testWithEnv
                "Failure mode 1"
                ``loader2-failure-case1``
                (fun env ->

                    task {
                        let path =
                            env.Entrypoints
                            |> Seq.map ProjectGraphEntryPoint

                        let loggers = env.Binlog.Loggers env.Binlog.File.Name

                        // Evaluation
                        use pc = projectCollection ()
                        let graph = ProjectLoader2.EvaluateAsGraphAllTfms(path, pc)

                        // Execution
                        let bp = BuildParameters(Loggers = loggers)
                        let bm = new BuildManagerSession()

                        let! (result: Result<GraphBuildResult, BuildErrors<GraphBuildResult>>) = ProjectLoader2.Execution(bm, graph, bp)
                        Expect.isError result "expected error"

                        match result with
                        | Ok _ -> failwith "expected error"
                        | Result.Error(BuildErrors.BuildErr(result, errorLogs)) ->
                            let results: (ProjectGraphNode * BuildErrors<BuildResult>) seq = GraphBuildResult.isolateFailures result errorLogs

                            let _, BuildErr(_, errors) =
                                results
                                |> Seq.head

                            let actualError =
                                errors
                                |> Seq.head
                                |> _.Message

                            Expect.equal actualError "Intentional failure" "expected error message"
                    }
                )


            testCaseTask
            |> testWithEnv
                "Cancellation"
                ``loader2-cancel-slow``
                (fun env ->
                    task {
                        let path =
                            env.Entrypoints
                            |> Seq.map ProjectGraphEntryPoint

                        // Evaluation
                        use pc = projectCollection ()

                        let graph = ProjectLoader2.EvaluateAsGraph(path, pc)

                        // Execution
                        let bp = BuildParameters(Loggers = env.Binlog.Loggers env.Binlog.File.Name)
                        let bm = new BuildManagerSession()
                        use cts = new CancellationTokenSource()

                        try
                            cts.CancelAfter(TimeSpan.FromSeconds 1.)

                            let! (_: Result<GraphBuildResult, BuildErrors<GraphBuildResult>>) = ProjectLoader2.Execution(bm, graph, bp, ct = cts.Token)
                            ()
                        with
                        | :? OperationCanceledException as oce -> Expect.equal oce.CancellationToken cts.Token "expected cancellation"
                        | e -> Exception.reraiseAny e

                    }
                )

        ]
