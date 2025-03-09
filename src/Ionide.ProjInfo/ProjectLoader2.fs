namespace Ionide.ProjInfo

open System
open System.Threading.Tasks
open System.Threading
open Microsoft.Build.Execution
open Microsoft.Build.Graph
open System.Collections.Generic
open Microsoft.Build.Evaluation
open Microsoft.Build.Framework

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

    let inline ofDict (dic) =
        dic
        |> Seq.map (|KeyValue|)
        |> Map.ofSeq

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
type BuildManagerSession(?bm: BuildManager, ?buildParameters: BuildParameters) =
    let locker = BuildManagerSession.locker
    let bm = defaultArg bm BuildManager.DefaultBuildManager
    let buildParameters = defaultArg buildParameters (BuildParameters(Loggers = [ ProjectLoader.ErrorLogger() ]))

    let tryGetErrorLogs () =
        buildParameters.Loggers
        |> Seq.tryPick (
            function
            | :? ProjectLoader.ErrorLogger as e -> Some e
            | _ -> None
        )
        |> Option.toList
        |> Seq.collect (fun e -> e.Errors)
        |> Seq.toList

    let lockAndStartBuild (ct: CancellationToken) (a: unit -> Task<_>) =
        task {
            use! _lock = locker.LockAsync ct
            use _ = bm.StartBuild(buildParameters, ct)
            return! a ()
        }

    member private x.determineBuildOutput<'e when BuildResultFailure<'e>>(result: BuildResult) =
        match result.OverallResult with
        | BuildResultCode.Success -> Ok result
        | _ -> Error('e.BuildFailure(result, tryGetErrorLogs ()))


    member private x.determineGraphBuildOutput<'e when GraphBuildResultFailure<'e>>(result: GraphBuildResult) =
        match result.OverallResult with
        | BuildResultCode.Success -> Ok result
        | _ -> Error('e.BuildFailure(result, tryGetErrorLogs ()))


    /// <summary>Submits a graph build request to the current build and starts it asynchronously.</summary>
    /// <param name="buildRequest">GraphBuildRequestData encapsulates all of the data needed to submit a graph build request.</param>
    /// <param name="ct">CancellationToken to cancel build submissions.</param>
    /// <returns>The BuildResult</returns>
    member x.BuildAsync(buildRequest: BuildRequestData, ?ct: CancellationToken) =
        let ct = defaultArg ct CancellationToken.None

        lockAndStartBuild ct
        <| fun () ->
            task {
                let tcs = TaskCompletionSource<_> TaskCreationOptions.RunContinuationsAsynchronously

                bm
                    .PendBuildRequest(buildRequest)
                    .ExecuteAsync(
                        (fun sub ->
                            let result = sub.BuildResult

                            match result.Exception with
                            | null -> tcs.SetResult(x.determineBuildOutput result)
                            | :? Microsoft.Build.Exceptions.BuildAbortedException when ct.IsCancellationRequested -> tcs.SetCanceled ct
                            | e -> tcs.SetException e
                        ),
                        buildRequest
                    )

                return! tcs.Task
            }

    /// <summary>Submits a graph build request to the current build and starts it asynchronously.</summary>
    /// <param name="graphBuildRequest">GraphBuildRequestData encapsulates all of the data needed to submit a graph build request.</param>
    /// <param name="ct">CancellationToken to cancel build submissions.</param>
    /// <returns>the GraphBuildResult</returns>
    member x.BuildAsync(graphBuildRequest: GraphBuildRequestData, ?ct: CancellationToken) =
        let ct = defaultArg ct CancellationToken.None

        lockAndStartBuild ct
        <| fun () ->
            task {
                let tcs = TaskCompletionSource<_> TaskCreationOptions.RunContinuationsAsynchronously

                bm
                    .PendBuildRequest(graphBuildRequest)
                    .ExecuteAsync(
                        (fun sub ->

                            let result = sub.BuildResult

                            match result.Exception with
                            | null -> tcs.SetResult(x.determineGraphBuildOutput result)
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


module ProjectLoading =

    let selectFirstTfm (projectPath: string) =
        let pi = ProjectInstance(projectPath)

        match
            pi.Properties
            |> ProjectPropertyInstance.tryFind "TargetFramework"
        with
        | Some v -> Some v
        | None ->
            match
                pi.Properties
                |> ProjectPropertyInstance.tryFind "TargetFrameworks"
            with
            | None -> None
            | Some tfms ->
                match tfms.Split(';') with
                | [||] -> None
                | tfms -> Array.tryHead tfms

    let defaultProjectInstanceFactory tfmSelector (projectPath: string) (xml: Dictionary<string, string>) (collection: ProjectCollection) =

        let tfm = tfmSelector projectPath

        let props = Map.union (Map.ofDict xml) (Map.ofDict collection.GlobalProperties)
        // |> Map.mapAddSome "TargetFramework" tfm

        let pi = ProjectInstance(projectPath, props, toolsVersion = null, projectCollection = collection)

        pi


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

    static member EvaluateAsGraph(entryProjectFile: string, ?globalProperties: IDictionary<string, string>, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let globalProperties = defaultArg globalProperties null
        ProjectLoader2.EvaluateAsGraph([ ProjectGraphEntryPoint(entryProjectFile, globalProperties = globalProperties) ], ?projectCollection = projectCollection, ?projectInstanceFactory = projectInstanceFactory, ?ct = ct)

    static member EvaluateAsGraph(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory, ?ct: CancellationToken) =
        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection
        let ct = defaultArg ct CancellationToken.None

        let projectInstanceFactory =
            defaultArg projectInstanceFactory (ProjectLoading.defaultProjectInstanceFactory ProjectLoading.selectFirstTfm)

        ProjectGraph(entryProjectFile, pc, projectInstanceFactory, ct)


    static member EvaluateAsGraphAllTfms(entryProjectFile: ProjectGraphEntryPoint seq, ?projectCollection: ProjectCollection, ?projectInstanceFactory) =

        let pc = defaultArg projectCollection ProjectCollection.GlobalProjectCollection

        let projectInstanceFactory =
            defaultArg projectInstanceFactory (ProjectLoading.defaultProjectInstanceFactory ProjectLoading.selectFirstTfm)

        let graph =
            ProjectLoader2.EvaluateAsGraph(entryProjectFile, projectCollection = pc, projectInstanceFactory = projectInstanceFactory)

        let targets = graph.ProjectNodes

        let inline tryGetTfmFromProps (node: ProjectGraphNode) =
            match node.ProjectInstance.GlobalProperties.TryGetValue "TargetFramework" with
            | true, tfm -> Some tfm
            | _ -> None

        // Then we only care about those with a TargetFramework
        let projects =
            targets
            |> Seq.choose (fun node ->
                tryGetTfmFromProps node
                |> Option.orElseWith (fun () ->
                    node.ProjectInstance.Properties
                    |> ProjectPropertyInstance.tryFind "TargetFramework"
                )
                |> Option.map (fun _ -> ProjectGraphEntryPoint(node.ProjectInstance.FullPath, globalProperties = node.ProjectInstance.GlobalProperties))
            )

        ProjectLoader2.EvaluateAsGraph(projects, projectCollection = pc, projectInstanceFactory = projectInstanceFactory)

    static member Execution(session: BuildManagerSession, graph: ProjectGraph, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                GraphBuildRequestData(projectGraph = graph, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?ct = ct)
        }

    static member Execution(session: BuildManagerSession, projectInstance: ProjectInstance, ?targetsToBuild: string array, ?flags: BuildRequestDataFlags, ?ct: CancellationToken) =
        task {
            let targetsToBuild = defaultArg targetsToBuild (ProjectLoader.designTimeBuildTargets false)

            let flags = defaultArg flags ProjectLoader2.DefaultFlags

            let request =
                BuildRequestData(projectInstance = projectInstance, targetsToBuild = targetsToBuild, hostServices = null, flags = flags)

            return! session.BuildAsync(request, ?ct = ct)
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
