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

/// <summary>
/// An awaitable wrapper around a task whose result is disposable. The wrapper is not disposable, so this prevents usage errors like "use _lock = myAsync()" when the appropriate usage should be "use! _lock = myAsync())".
/// </summary>
[<Struct>]
type AwaitableDisposable<'T when 'T :> IDisposable>(t: Task<'T>) =
    member x.GetAwaiter() = t.GetAwaiter()
    member x.AsTask() = t
    static member op_Implicit(source: AwaitableDisposable<'T>) = source.AsTask()

[<AutoOpenAttribute>]
module SemaphoreSlimExtensions =

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


module Map =

    let mapAddSome key value map =
        match value with
        | Some v -> Map.add key v map
        | None -> map

    let union loses wins =
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


type BuildResultFailure<'e> =
    static abstract BuildFailure: BuildResult * BuildErrorEventArgs list -> 'e

type GraphBuildResultFailure<'e> =
    static abstract BuildFailure: GraphBuildResult * BuildErrorEventArgs list -> 'e


module GraphBuildResult =
    let resultsByNode<'e when BuildResultFailure<'e>> (result: GraphBuildResult, errorLogs: BuildErrorEventArgs list) =
        result.ResultsByNode
        |> Seq.map (fun (KeyValue(k, v)) ->
            match v.OverallResult with
            | BuildResultCode.Success -> KeyValuePair(k, Ok v)
            | _ ->
                let logs =
                    errorLogs
                    |> List.filter (fun e -> e.ProjectFile = k.ProjectInstance.FullPath)

                KeyValuePair(k, Error('e.BuildFailure(v, logs)))
        )
        |> Dictionary<_, _>

    let isolateFailures (result: GraphBuildResult, errorLogs: BuildErrorEventArgs list) =
        resultsByNode (result, errorLogs)
        |> Seq.choose (fun (KeyValue(k, v)) ->
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

    member private x.determineBuildOutput<'e when BuildResultFailure<'e>>(buildParameters, result: BuildResult) =
        match result.OverallResult with
        | BuildResultCode.Success -> Ok result
        | _ -> Error('e.BuildFailure(result, tryGetErrorLogs buildParameters))


    member private x.determineGraphBuildOutput<'e when GraphBuildResultFailure<'e>>(buildParameters, result: GraphBuildResult) =
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
                            | :? Microsoft.Build.Exceptions.BuildAbortedException when ct.IsCancellationRequested -> tcs.SetCanceled ct
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
                            | :? Microsoft.Build.Exceptions.BuildAbortedException when ct.IsCancellationRequested -> tcs.SetCanceled ct
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

    let parse (tfms: string) =
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


type ProjectMap<'a> = Map<ProjectPath, Map<TargetFramework, 'a>>

module ProjectMap =

    let map (f: ProjectPath -> TargetFramework -> 'a -> 'a0) (m: ProjectMap<'a>) =
        m
        |> Map.map (fun k -> Map.map (f k))

type ProjectProjectMap = ProjectMap<Project>
type ProjectGraphMap = ProjectMap<ProjectGraphNode>

module ProjectLoading =

    let getAllTfms (projectPath: ProjectPath) =
        let pi = ProjectInstance projectPath

        pi.Properties
        |> (ProjectPropertyInstance.tryFind "TargetFramework"
            >> Option.map Array.singleton)
        |> Option.orElseWith (fun () ->
            pi.Properties
            |> ProjectPropertyInstance.tryFind "TargetFrameworks"
            |> Option.bind TargetFrameworks.parse
        )

    let selectFirstTfm (projectPath: string) =
        getAllTfms projectPath
        |> Option.bind Array.tryHead

    let defaultProjectInstanceFactory (projectPath: string) (xml: Dictionary<string, string>) (collection: ProjectCollection) =
        let props = Map.union (Map.ofDict xml) (Map.ofDict collection.GlobalProperties)
        ProjectInstance(projectPath, props, toolsVersion = null, projectCollection = collection)


type ProjectLoader2 =

    static member DefaultFlags =
        BuildRequestDataFlags.SkipNonexistentTargets
        ||| BuildRequestDataFlags.ClearCachesAfterBuild
        ||| BuildRequestDataFlags.ProvideProjectStateAfterBuild
        ||| BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports
        ||| BuildRequestDataFlags.ReplaceExistingProjectInstance

    static member EvaluateAsProject(entryProjectFile: string, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =
        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection
        let globalProperties = defaultArg globalProperties null
        pc.LoadProject(entryProjectFile, globalProperties = globalProperties, toolsVersion = null)

    static member EvaluateAsProjects(entryProjectFiles: string seq, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =
        entryProjectFiles
        |> Seq.map (fun file -> ProjectLoader2.EvaluateAsProject(file, ?globalProperties = globalProperties, ?projectCollection = projectCollection))

    static member EvaluateAsProjectsAllTfms(entryProjectFiles: string seq, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection) =
        let globalProperties =
            globalProperties
            |> Option.map Map.ofDict
            |> Option.defaultValue Map.empty

        entryProjectFiles
        |> Seq.collect (fun path ->
            ProjectLoading.getAllTfms path
            |> Option.toArray
            |> Array.collect (
                Array.map (fun tfm ->
                    ProjectLoader2.EvaluateAsProject(
                        path,
                        globalProperties =
                            (globalProperties
                             |> Map.add "TargetFramework" tfm
                             |> Map.copyToDict),
                        ?projectCollection = projectCollection
                    )
                )
            )
        )

    static member EvaluateAsGraph(entryProjectFile: string, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let globalProperties = defaultArg globalProperties null
        ProjectLoader2.EvaluateAsGraph([ ProjectGraphEntryPoint(entryProjectFile, globalProperties = globalProperties) ], ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory, ?ct = ct)

    static member EvaluateAsGraph(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection
        let ct = defaultArg ct CancellationToken.None

        let projectInstanceFactory = defaultArg projectInstanceFactory ProjectLoading.defaultProjectInstanceFactory

        ProjectGraph(entryProjectFile, pc, projectInstanceFactory, ct)


    static member EvaluateAsGraphAllTfms(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory) =
        let graph =
            ProjectLoader2.EvaluateAsGraph(entryProjectFile, ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory)

        let inline tryGetTfmFromProps (node: ProjectGraphNode) =
            match node.ProjectInstance.GlobalProperties.TryGetValue "TargetFramework" with
            | true, tfm -> Some tfm
            | _ -> None

        // Then we only care about those with a TargetFramework
        let projects =
            graph.ProjectNodes
            |> Seq.choose (fun node ->
                tryGetTfmFromProps node
                |> Option.orElseWith (fun () ->
                    node.ProjectInstance.Properties
                    |> ProjectPropertyInstance.tryFind "TargetFramework"
                )
                |> Option.map (fun _ -> ProjectGraphEntryPoint(node.ProjectInstance.FullPath, globalProperties = node.ProjectInstance.GlobalProperties))
            )

        ProjectLoader2.EvaluateAsGraph(projects, ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory)

    static member Execution(session: BuildManagerSession, graph: ProjectGraph, ?buildParameters: BuildParameters, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                GraphBuildRequestData(projectGraph = graph, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?buildParameters = buildParameters, ?ct = ct)
        }

    static member Execution(session: BuildManagerSession, projectInstance: ProjectInstance, ?buildParameters: BuildParameters, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                BuildRequestData(projectInstance = projectInstance, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?buildParameters = buildParameters, ?ct = ct)
        }

    static member ExecutionWalkReferences<'e when BuildResultFailure<'e>>
        (session: BuildManagerSession, projects: Project  seq, buildParameters: Project -> BuildParameters option, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken)
        =
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
                        |> Seq.iter projectsToVisit.Enqueue
                    | _ -> ()
                    |> ignore

            return
                visited.Values
                |> Seq.toArray
        }

    static member GetProjectInstance(buildResult: BuildResult) = buildResult.ProjectStateAfterBuild

    static member GetProjectInstance(buildResults: BuildResult seq) =
        buildResults
        |> Seq.map ProjectLoader2.GetProjectInstance

    static member GetProjectInstances(graphBuildResult: GraphBuildResult) =
        graphBuildResult.ResultsByNode
        |> Seq.map (fun (KeyValue(node, result)) -> ProjectLoader2.GetProjectInstance result)

    static member Parse(graphBuildResult: GraphBuildResult) =
        graphBuildResult
        |> ProjectLoader2.GetProjectInstances
        |> Seq.map ProjectLoader2.Parse

    static member Parse(buildResult: BuildResult) =
        buildResult
        |> ProjectLoader2.GetProjectInstance
        |> ProjectLoader2.Parse

    static member Parse(projectInstances: ProjectInstance seq) =
        projectInstances
        |> Seq.toArray
        |> Array.Parallel.map ProjectLoader2.Parse

    static member Parse(projectInstances: ProjectInstance) =
        ProjectLoader.getLoadedProjectInfo projectInstances.FullPath [] (ProjectLoader.StandardProject projectInstances)
