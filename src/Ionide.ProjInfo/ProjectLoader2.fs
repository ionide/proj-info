namespace Ionide.ProjInfo

open System
open System.Threading.Tasks
open System.Threading
open Microsoft.Build.Execution
open Microsoft.Build.Graph
open System.Collections.Generic
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework
open ProjectLoader

[<AutoOpenAttribute>]
module SemaphoreSlimExtensions =

    /// <summary>
    /// An awaitable wrapper around a task whose result is disposable. The wrapper is not disposable, so this prevents usage errors like "use _lock = myAsync()" when the appropriate usage should be "use! _lock = myAsync())".
    /// </summary>
    [<Struct>]
    type AwaitableDisposable<'T when 'T :> IDisposable>(t: Task<'T>) =
        member x.GetAwaiter() = t.GetAwaiter()
        member x.AsTask() = t
        static member op_Implicit(source: AwaitableDisposable<'T>) = source.AsTask()


    type SemaphoreSlim with

        member x.LockAsync(?ct: CancellationToken) =
            AwaitableDisposable(
                task {
                    let ct = defaultArg ct CancellationToken.None
                    let t = x.WaitAsync(ct)

                    do! t

                    return
                        { new IDisposable with
                            member _.Dispose() =
                                // only release if the task completed successfully
                                // otherwise, we could be releasing a semaphore that was never acquired
                                if t.Status = TaskStatus.RanToCompletion then
                                    x.Release()
                                    |> ignore
                        }
                }
            )


module internal Map =

    let inline mapAddSome key value map =
        match value with
        | Some v -> Map.add key v map
        | None -> map

    let inline union loses wins =
        Map.fold (fun acc key value -> Map.add key value acc) loses wins

    let inline ofDict dictionary =
        dictionary
        |> Seq.map (|KeyValue|)
        |> Map.ofSeq


    let inline copyToDict (map: Map<_, _>) =
        // Have to use a mutable dictionary here because the F# Map doesn't have an Add method
        let dictionary = Dictionary<_, _>()

        for KeyValue(k, v) in map do
            dictionary.Add(k, v)

        dictionary :> IDictionary<_, _>


module BuildErrorEventArgs =

    let messages (e: BuildErrorEventArgs seq) =
        e
        |> Seq.sortBy (fun e -> e.Timestamp)
        |> Seq.map (fun e -> $"{e.ProjectFile} {e.Message}")
        |> String.concat "\n"

[<AutoOpenAttribute>]
module internal BuildManagerExtensions =

    type BuildManager with

        /// <summary>
        /// Prepares the BuildManager to receive build requests.
        ///
        /// Returns disposable that signals that no more build requests are expected (or allowed) and the BuildManager may clean up. This call blocks until all currently pending requests are complete.
        /// </summary>
        /// <param name="parameters">The build parameters. May be null</param>
        /// <param name="ct">CancellationToken to cancel build submissions.</param>
        /// <returns>Disposable calling EndBuild.</returns>
        member bm.StartBuild(?parameters: BuildParameters, ?ct: CancellationToken) =
            let parameters = defaultArg parameters null
            let ct = defaultArg ct CancellationToken.None
            ct.ThrowIfCancellationRequested()
            bm.BeginBuild parameters

            let cancelSubmissions =
                ct.Register(fun () ->
                    // https://github.com/dotnet/msbuild/issues/3397 :(
                    bm.CancelAllSubmissions()
                )

            { new IDisposable with
                member _.Dispose() =
                    cancelSubmissions.Dispose()
                    bm.EndBuild()
            }

module internal BuildManagerSession =
    // multiple concurrent builds cannot be issued to BuildManager
    // Creating SemaphoreSlim here so we only have one per application
    let internal locker = new SemaphoreSlim(1, 1)


type BuildResultFailure<'e, 'buildResult> =
    static abstract BuildFailure: 'buildResult * BuildErrorEventArgs list -> 'e

module GraphBuildResult =

    /// <summary>
    /// Groups build results by their associated project nodes.
    /// </summary>
    /// <param name="result">The GraphBuildResult to group.</param>
    /// <param name="errorLogs">The error logs from the failed build.</param>
    /// <returns>A dictionary where the key is the project node and the value is either a successful BuildResult or an error containing the failure details.</returns>
    let resultsByNode<'e when BuildResultFailure<'e, BuildResult>> (result: GraphBuildResult) (errorLogs: BuildErrorEventArgs list) =
        let errorLogsMap =
            lazy
                (errorLogs
                 |> List.groupBy (fun e -> e.ProjectFile)
                 |> Map.ofList)

        result.ResultsByNode
        |> Seq.map (fun (KeyValue(k, v)) ->
            match v.OverallResult with
            | BuildResultCode.Success -> k, Ok v
            | _ ->
                let logs =
                    errorLogsMap.Value
                    |> Map.tryFind k.ProjectInstance.FullPath
                    |> Option.defaultValue []

                k, Error('e.BuildFailure(v, logs))
        )

    /// <summary>
    /// Isolates failures from a GraphBuildResult, returning a sequence of KeyValuePairs where the value is an Error.
    /// </summary>
    /// <param name="result">The GraphBuildResult to isolate failures from.</param>
    /// <param name="errorLogs">The error logs from the failed build.</param>
    let isolateFailures (result: GraphBuildResult) (errorLogs: BuildErrorEventArgs list) =
        resultsByNode result errorLogs
        |> Seq.choose (fun (k, v) ->
            match v with
            | Ok v -> None
            | Error e -> Some(k, e)
        )

/// <summary>
/// Uses <see cref="T:Microsoft.Build.Execution.BuildManager"/> to run builds.
/// This should be treated as a singleton because the BuildManager only allows one build request running at a time.
/// </summary>
type BuildManagerSession(?bm: BuildManager) =
    let locker = BuildManagerSession.locker
    let bm = defaultArg bm BuildManager.DefaultBuildManager

    let tryGetErrorLogs (buildParameters: BuildParameters) =
        buildParameters.Loggers
        |> Seq.tryPick (
            function
            | :? ProjectLoader.ErrorLogger as e -> Some e
            | _ -> None
        )
        |> Option.toList
        |> Seq.collect (fun e -> e.Errors)
        |> Seq.toList

    let lockAndStartBuild (ct: CancellationToken) buildParameters (a: unit -> Task<_>) =
        task {
            use! _lock = locker.LockAsync ct
            use _ = bm.StartBuild(buildParameters, ct)
            return! a ()
        }

    member private x.determineBuildOutput<'e when BuildResultFailure<'e, BuildResult>>(buildParameters, result: BuildResult) =
        match result.OverallResult with
        | BuildResultCode.Success -> Ok result
        | _ -> Error('e.BuildFailure(result, tryGetErrorLogs buildParameters))

    member private x.determineGraphBuildOutput<'e when BuildResultFailure<'e, GraphBuildResult>>(buildParameters, result: GraphBuildResult) =
        match result.OverallResult with
        | BuildResultCode.Success -> Ok result
        | _ -> Error('e.BuildFailure(result, tryGetErrorLogs buildParameters))


    /// <summary>Submits a graph build request to the current build and starts it asynchronously.</summary>
    /// <param name="buildRequest">GraphBuildRequestData encapsulates all of the data needed to submit a graph build request.</param>
    /// <param name="buildParameters">All of the settings which must be specified to start a build</param>
    /// <param name="ct">CancellationToken to cancel build submissions.</param>
    /// <returns>The BuildResult</returns>
    member x.BuildAsync(buildRequest: BuildRequestData, ?buildParameters: BuildParameters, ?ct: CancellationToken) =
        let ct = defaultArg ct CancellationToken.None

        let buildParameters =
            defaultArg
                buildParameters
                (BuildParameters(
                    Loggers = [
                        msBuildToLogProvider ()
                        ProjectLoader.ErrorLogger()
                    ]
                ))

        lockAndStartBuild ct buildParameters
        <| fun () ->
            task {
                let tcs = TaskCompletionSource<_> TaskCreationOptions.RunContinuationsAsynchronously

                bm
                    .PendBuildRequest(buildRequest)
                    .ExecuteAsync(
                        (fun sub ->
                            let result = sub.BuildResult

                            match result.Exception with
                            | null -> tcs.SetResult(x.determineBuildOutput (buildParameters, result))
                            | :? Microsoft.Build.Exceptions.BuildAbortedException as bae when ct.IsCancellationRequested ->
                                OperationCanceledException("Build was cancelled", bae, ct)
                                |> tcs.SetException
                            | e -> tcs.SetException e
                        ),
                        buildRequest
                    )

                return! tcs.Task
            }

    /// <summary>Submits a graph build request to the current build and starts it asynchronously.</summary>
    /// <param name="graphBuildRequest">GraphBuildRequestData encapsulates all of the data needed to submit a graph build request.</param>
    /// <param name="buildParameters">All of the settings which must be specified to start a build</param>
    /// <param name="ct">CancellationToken to cancel build submissions.</param>
    /// <returns>the GraphBuildResult</returns>
    member x.BuildAsync(graphBuildRequest: GraphBuildRequestData, ?buildParameters: BuildParameters, ?ct: CancellationToken) =
        let ct = defaultArg ct CancellationToken.None
        let buildParameters = defaultArg buildParameters (BuildParameters(Loggers = [ ProjectLoader.ErrorLogger() ]))

        lockAndStartBuild ct buildParameters
        <| fun () ->
            task {
                let tcs = TaskCompletionSource<_> TaskCreationOptions.RunContinuationsAsynchronously

                bm
                    .PendBuildRequest(graphBuildRequest)
                    .ExecuteAsync(
                        (fun sub ->

                            let result = sub.BuildResult

                            match result.Exception with
                            | null -> tcs.SetResult(x.determineGraphBuildOutput (buildParameters, result))
                            | :? Microsoft.Build.Exceptions.BuildAbortedException as bae when ct.IsCancellationRequested ->
                                OperationCanceledException("Build was cancelled", bae, ct)
                                |> tcs.SetException
                            | e -> tcs.SetException e

                        ),
                        graphBuildRequest
                    )

                return! tcs.Task
            }


module ProjectPropertyInstance =
    let tryFind (name: string) (properties: ProjectPropertyInstance seq) =
        properties
        |> Seq.tryFind (fun p -> p.Name = name)
        |> Option.map (fun v -> v.EvaluatedValue)


type ProjectPath = string
type TargetFramework = string
type TargetFrameworks = string array

module TargetFrameworks =

    /// <summary>
    /// Parses a string containing TargetFrameworks into an array of TargetFrameworks.
    /// </summary>
    /// <param name="tfms">The string containing TargetFrameworks, separated by semicolons.</param>
    /// <returns>An array of TargetFrameworks, or None if the input is null or empty.</returns>
    /// <remarks>
    /// This takes a string of the form "net5.0;net6.0;net7.0" and splits it into an array of TargetFrameworks.
    /// </remarks>
    let parse (tfms: string) : TargetFramework array option =
        tfms
        |> Option.ofObj
        |> Option.bind (fun tfms ->
            tfms.Split(
                ';',
                StringSplitOptions.TrimEntries
                ||| StringSplitOptions.RemoveEmptyEntries
            )
            |> Option.ofObj
        )


// type ProjectMap<'a> = Map<ProjectPath, Map<TargetFramework, 'a>>

// module ProjectMap =

//     let map (f: ProjectPath -> TargetFramework -> 'a -> 'a0) (m: ProjectMap<'a>) =
//         m
//         |> Map.map (fun k -> Map.map (f k))

// type ProjectProjectMap = ProjectMap<Project>
// type ProjectGraphMap = ProjectMap<ProjectGraphNode>

module internal ProjectLoading =

    // let getAllTfms (projectPath: ProjectPath) pc props =
    //     let p = findOrCreateMatchingProject projectPath pc props
    //     let pi = p.CreateProjectInstance()

    //     pi.Properties
    //     |> (ProjectPropertyInstance.tryFind "TargetFramework"
    //         >> Option.map Array.singleton)
    //     |> Option.orElseWith (fun () ->
    //         pi.Properties
    //         |> ProjectPropertyInstance.tryFind "TargetFrameworks"
    //         |> Option.bind TargetFrameworks.parse
    //     )

    // let selectFirstTfm (projectPath: string) =
    //     getAllTfms projectPath
    //     |> Option.bind Array.tryHead

    let inline defaultProjectInstanceFactory (projectPath: string) (xml: Dictionary<string, string>) (collection: ProjectCollection) =
        let props = Map.union (Map.ofDict xml) (Map.ofDict collection.GlobalProperties)
        ProjectInstance(projectPath, props, toolsVersion = null, projectCollection = collection)


type ProjectLoader2 =

    /// <summary>
    /// Default flags for build requests.
    ///
    /// BuildRequestDataFlags.SkipNonexistentTargets
    /// ||| BuildRequestDataFlags.ClearCachesAfterBuild
    /// ||| BuildRequestDataFlags.ProvideProjectStateAfterBuild
    /// ||| BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports
    /// ||| BuildRequestDataFlags.ReplaceExistingProjectInstance
    /// </summary>
    static member DefaultFlags =
        BuildRequestDataFlags.SkipNonexistentTargets
        ||| BuildRequestDataFlags.ClearCachesAfterBuild
        ||| BuildRequestDataFlags.ProvideProjectStateAfterBuild
        ||| BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports
        ||| BuildRequestDataFlags.ReplaceExistingProjectInstance


    /// <summary>
    /// Finds or creates a project matching the specified entry project file and global properties.
    /// </summary>
    /// <param name="entryProjectFile">The project file to match.</param>
    /// <param name="globalProperties">Optional global properties to apply to the project.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <returns>The evaluated project.</returns>
    /// <remarks>
    /// This method evaluates the project file and returns the corresponding project.
    /// It does not check for TargetFramework or TargetFrameworks properties; it simply returns the project as is.
    /// </remarks>
    static member EvaluateAsProject(entryProjectFile: string, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =
        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection
        let globalProperties = defaultArg globalProperties (new Dictionary<string, string>())
        findOrCreateMatchingProject entryProjectFile pc globalProperties

    /// <summary>
    /// Evaluates a sequence of project files, returning a sequence of projects.
    /// </summary>
    /// <param name="entryProjectFiles">The project files to evaluate.</param>
    /// <param name="globalProperties">Optional global properties to apply to each project.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <returns>A sequence of projects, each corresponding to a project file.</returns>
    /// <remarks>
    /// This method evaluates each project file and returns the corresponding project.
    /// It does not check for TargetFramework or TargetFrameworks properties; it simply returns the project as is.
    /// </remarks>
    static member EvaluateAsProjects(entryProjectFiles: string seq, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =
        entryProjectFiles
        |> Seq.map (fun file -> ProjectLoader2.EvaluateAsProject(file, ?globalProperties = globalProperties, ?projectCollection = projectCollection))

    /// <summary>
    /// Evaluates a sequence of project files, returning a sequence of projects for each TargetFramework
    /// or TargetFrameworks defined in the project files.
    /// </summary>
    /// <param name="entryProjectFiles">The project files to evaluate.</param>
    /// <param name="globalProperties">Optional global properties to apply to each project.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <returns>A sequence of projects, each corresponding to a specific TargetFramework or TargetFrameworks defined in the project files.</returns>
    /// <remarks>
    /// This method evaluates each project file and checks for the presence of a "TargetFramework"
    /// property. If it exists, the project is returned as is. If it does not exist, it checks for the "TargetFrameworks"
    /// property and splits it into individual TargetFrameworks. For each TargetFramework, it creates a new project
    /// with the "TargetFramework" global property set to that TargetFramework.
    /// </remarks>
    static member EvaluateAsProjectsAllTfms(entryProjectFiles: string seq, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =

        let globalPropertiesMap =
            lazy
                (globalProperties
                 |> Option.map Map.ofDict
                 |> Option.defaultValue Map.empty)

        entryProjectFiles
        |> Seq.collect (fun path ->
            let p = ProjectLoader2.EvaluateAsProject(path, ?globalProperties = globalProperties, ?projectCollection = projectCollection)
            let pi = p.CreateProjectInstance()

            match
                pi.Properties
                |> ProjectPropertyInstance.tryFind "TargetFramework"
            with
            | Some _ -> Seq.singleton p
            | None ->
                let tfms =
                    pi.Properties
                    |> ProjectPropertyInstance.tryFind "TargetFrameworks"
                    |> Option.bind TargetFrameworks.parse
                    |> Option.defaultValue Array.empty

                tfms
                |> Seq.map (fun tfm ->
                    ProjectLoader2.EvaluateAsProject(
                        path,
                        globalProperties =
                            (globalPropertiesMap.Value
                             |> Map.add "TargetFramework" tfm
                             |> Map.copyToDict),
                        ?projectCollection = projectCollection
                    )
                )
        )

    /// <summary>
    /// Evaluates a project graph based on the specified entry project file a
    /// </summary>
    /// <param name="entryProjectFile">The entry project file to evaluate.</param>
    /// <param name="globalProperties">Optional global properties to apply to the project.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <param name="projectInstanceFactory">Optional factory function to create project instances.</param>
    /// <param name="ct">Optional cancellation token to cancel the evaluation.</param>
    /// <returns>A project graph representing the evaluated project.</returns>
    /// <remarks>
    /// This method evaluates the project file and returns a project graph.
    /// It does not check for TargetFramework or TargetFrameworks properties; it simply returns the project graph as is.
    /// </remarks>
    static member EvaluateAsGraph(entryProjectFile: string, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let globalProperties = defaultArg globalProperties null
        ProjectLoader2.EvaluateAsGraph([ ProjectGraphEntryPoint(entryProjectFile, globalProperties = globalProperties) ], ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory, ?ct = ct)

    /// <summary>
    /// Evaluates a project graph based on the specified entry project files
    /// </summary>
    /// <param name="entryProjectFile">The entry project files to evaluate.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <param name="projectInstanceFactory">Optional factory function to create project instances.</param>
    /// <param name="ct">Optional cancellation token to cancel the evaluation.</param>
    /// <returns>A project graph representing the evaluated projects.</returns>
    /// <remarks>
    /// This method evaluates the project files and returns a project graph.
    /// It does not check for TargetFramework or TargetFrameworks properties; it simply returns the project graph as is.
    /// </remarks>
    static member EvaluateAsGraph(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection
        let ct = defaultArg ct CancellationToken.None

        let projectInstanceFactory =
            let inline defaultFunc path xml pc =
                // (findOrCreateMatchingProject path pc xml).CreateProjectInstance()
                ProjectLoading.defaultProjectInstanceFactory path xml pc

            defaultArg projectInstanceFactory defaultFunc

        ProjectGraph(entryProjectFile, pc, projectInstanceFactory, ct)


    /// <summary>
    /// Evaluates a project graph based on the specified entry project files, returning a ProjectGraph containing
    /// projects for each TargetFramework or TargetFrameworks defined in the project files.
    /// </summary>
    /// <param name="entryProjectFile">The entry project files to evaluate.</param>
    /// <param name="projectCollection">Optional project collection to use for evaluation.</param>
    /// <param name="projectInstanceFactory">Optional factory function to create project instances.</param>
    /// <returns>A project graph representing the evaluated projects, each corresponding to a specific TargetFramework
    /// or TargetFrameworks defined in the project files.</returns>
    /// <remarks>
    ///
    /// MSBuild's ProjectGraph natively handles multi-targeting by creating "outer build" nodes (with TargetFrameworks)
    /// that reference "inner build" nodes (with individual TargetFramework values). However, when building a graph,
    /// only entry point nodes are built directly. This method performs two evaluations:
    /// 1. First pass: Discover all nodes in the graph (including outer/inner builds and all references)
    /// 2. Second pass: Create a new graph with only nodes that have a TargetFramework property set
    ///
    /// This ensures that all inner builds are treated as entry points and get built directly, which is required
    /// for design-time analysis scenarios where we need build results for each TFM.
    /// </remarks>
    static member EvaluateAsGraphAllTfms(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory) =
        // MSBuild's ProjectGraph handles multi-TFM projects by creating:
        // - OuterBuild nodes: TargetFramework is empty, TargetFrameworks is set (dispatchers)
        // - InnerBuild nodes: TargetFramework is set (actual builds per TFM)
        // - NonMultitargeting nodes: Neither property meaningfully set
        //
        // First pass: Evaluate to discover all project nodes including inner builds created from outer builds
        let graph =
            ProjectLoader2.EvaluateAsGraph(entryProjectFile, ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory)

        // Helper to get TargetFramework from global properties or project properties
        let tryGetTargetFramework (node: ProjectGraphNode) =
            match node.ProjectInstance.GlobalProperties.TryGetValue "TargetFramework" with
            | true, tfm when not (String.IsNullOrWhiteSpace tfm) -> Some tfm
            | _ ->
                node.ProjectInstance.Properties
                |> ProjectPropertyInstance.tryFind "TargetFramework"
                |> Option.filter (
                    not
                    << String.IsNullOrWhiteSpace
                )

        // Extract only nodes with a TargetFramework as new entry points
        // This filters out outer builds (which have TargetFrameworks but not TargetFramework)
        // and includes inner builds (which have TargetFramework set)
        let innerBuildEntryPoints =
            graph.ProjectNodes
            |> Seq.choose (fun node ->
                tryGetTargetFramework node
                |> Option.map (fun _ -> ProjectGraphEntryPoint(node.ProjectInstance.FullPath, globalProperties = node.ProjectInstance.GlobalProperties))
            )

        // Second pass: Re-evaluate with inner builds as entry points
        // This ensures all inner builds are built directly and appear in ResultsByNode
        ProjectLoader2.EvaluateAsGraph(innerBuildEntryPoints, ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory)

    /// <summary>
    /// Executes a build request against the BuildManagerSession.
    /// </summary>
    /// <param name="session">The BuildManagerSession to use for the build.</param
    /// ><param name="graph">The project graph to build.</param>
    /// <param name="buildParameters">Optional build parameters to use for the build.</param
    /// ><param name="targetsToBuild">Optional targets to build. Defaults to design-time build targets.</param>
    /// <param name="flags">Optional flags for the build request. Defaults to ProjectLoader2.DefaultFlags.</param>
    /// <param name="ct">Optional cancellation token to cancel the
    /// build.</param>
    /// <returns>A either a GraphBuildResult or an error containing the failed build and message.</returns>
    static member Execution(session: BuildManagerSession, graph: ProjectGraph, ?buildParameters: BuildParameters, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                GraphBuildRequestData(projectGraph = graph, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?buildParameters = buildParameters, ?ct = ct)
        }

    /// <summary>
    /// Executes a build request against the BuildManagerSession.
    /// </summary>
    /// <param name="session">The BuildManagerSession to use for the build.</param
    /// ><param name="projectInstance">The project instance to build.</param>
    /// <param name="buildParameters">Optional build parameters to use for the build.</param
    /// ><param name="targetsToBuild">Optional targets to build. Defaults to design-time build targets.</param>
    /// <param name="flags">Optional flags for the build request. Defaults to ProjectLoader2.DefaultFlags.</param>
    /// <param name="ct">Optional cancellation token to cancel the
    /// build.</param>
    /// <returns>A either a BuildResult or an error containing the failed build and message.</returns>
    static member Execution(session: BuildManagerSession, projectInstance: ProjectInstance, ?buildParameters: BuildParameters, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                BuildRequestData(projectInstance = projectInstance, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?buildParameters = buildParameters, ?ct = ct)
        }

    /// <summary>
    /// Walks the project references of the given projects and executes a build for each project.
    /// </summary>
    /// <param name="session">The BuildManagerSession to use for the build.</param
    /// ><param name="projects">The projects to walk references for.</param>
    /// <param name="buildParameters">Function to get build parameters for each project.</param
    /// ><param name="targetsToBuild">Optional targets to build. Defaults to design-time build targets.</param>
    /// <param name="flags">Optional flags for the build request. Defaults to ProjectLoader2.DefaultFlags.</param>
    /// <param name="ct">Optional cancellation token to cancel the build.</param>
    /// <returns>A task that returns an array of BuildResult or an error containing the failed build and message.</returns>
    /// <remarks>
    /// This method will visit each project, build it, and then recursively visit its references.
    /// It will return an array of BuildResult for each project that was built.
    /// If a project has already been visited, it will not be visited again.
    ///
    /// This is useful for scenarios where you want to build a project and all of its references.
    /// </remarks>
    static member ExecutionWalkReferences(session: BuildManagerSession, projects: Project seq, buildParameters: Project -> BuildParameters option, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let projectsToVisit = Queue<Project> projects

            let visited = Dictionary<Project, Result<BuildResult, 'e>>()

            while projectsToVisit.Count > 0 do
                let p = projectsToVisit.Dequeue()

                match visited.TryGetValue p with
                | true, _ -> ()
                | _ ->

                    let projectInstance = p.CreateProjectInstance()
                    let bp = buildParameters p

                    let! result = ProjectLoader2.Execution(session, projectInstance, ?buildParameters = bp, ?targetsToBuild = targetsToBuild, ?flags = flags, ?ct = ct)
                    visited.Add(p, result)

                    match result with
                    | Ok result ->
                        let references =
                            result.ProjectStateAfterBuild.Items
                            |> Seq.choose (fun item ->
                                if
                                    item.ItemType = "_MSBuildProjectReferenceExistent"
                                    && item.HasMetadata "FullPath"
                                then
                                    Some(item.GetMetadataValue "FullPath")
                                else
                                    None
                            )

                        ProjectLoader2.EvaluateAsProjectsAllTfms(references, projectCollection = p.ProjectCollection)
                        |> Seq.filter (
                            visited.ContainsKey
                            >> not
                        )
                        |> Seq.iter projectsToVisit.Enqueue
                    | _ -> ()

            return
                visited.Values
                |> Seq.toArray
        }

    /// <summary>
    /// Gets the project instance from a BuildResult.
    /// </summary>
    /// <param name="buildResult">The BuildResult to get the project instance from.</param>
    /// <returns>The project instance from the BuildResult.</returns>

    static member GetProjectInstance(buildResult: BuildResult) = buildResult.ProjectStateAfterBuild

    /// <summary>
    /// Gets the project instance from a sequence of BuildResults.
    /// </summary>
    /// <param name="buildResults">The sequence of BuildResults to get the project instances from.</param>
    /// <returns>The project instances from the sequence of BuildResults.</returns>
    static member GetProjectInstances(buildResults: BuildResult seq) =
        buildResults
        |> Seq.map ProjectLoader2.GetProjectInstance

    /// <summary>
    /// Gets the project instances from a GraphBuildResult.
    /// </summary>
    /// <param name="graphBuildResult">The GraphBuildResult to get the project instances from.</param>
    /// <returns>The project instances from the GraphBuildResult.</returns>
    static member GetProjectInstances(graphBuildResult: GraphBuildResult) =
        graphBuildResult.ResultsByNode
        |> Seq.map (fun (KeyValue(node, result)) -> ProjectLoader2.GetProjectInstance result)


    /// <summary>
    /// Parses a BuildResult or GraphBuildResult into a ProjectInfo.
    /// </summary>
    /// <param name="graphBuildResult">The BuildResult or GraphBuildResult to parse.</param>
    /// <returns>A sequence of ProjectInfo parsed from the BuildResult or GraphBuildResult.</returns>
    static member Parse(graphBuildResult: GraphBuildResult) =
        graphBuildResult
        |> ProjectLoader2.GetProjectInstances
        |> Seq.map ProjectLoader2.Parse

    /// <summary>
    /// Parses a BuildResult into a ProjectInfo.
    /// </summary>
    /// <param name="buildResult">The BuildResult to parse.</param>
    /// <returns>A ProjectInfo parsed from the BuildResult.</returns>
    static member Parse(buildResult: BuildResult) =
        buildResult
        |> ProjectLoader2.GetProjectInstance
        |> ProjectLoader2.Parse

    /// <summary>
    /// Parses a sequence of ProjectInstances into a sequence of ProjectInfo.
    /// </summary>
    /// <param name="projectInstances">The sequence of ProjectInstances to parse.</param>
    /// <returns>A sequence of ProjectInfo parsed from the ProjectInstances.</returns>
    static member Parse(projectInstances: ProjectInstance seq) =
        projectInstances
        |> Seq.toArray
        |> Array.Parallel.map ProjectLoader2.Parse

    /// <summary>
    /// Parses a ProjectInstance into a ProjectInfo.
    /// </summary>
    /// <param name="projectInstances">The ProjectInstance to parse.</param>
    /// <returns>A ProjectInfo parsed from the ProjectInstance.</returns>
    static member Parse(projectInstances: ProjectInstance) =
        ProjectLoader.getLoadedProjectInfo projectInstances.FullPath [] (ProjectLoader.StandardProject projectInstances)
