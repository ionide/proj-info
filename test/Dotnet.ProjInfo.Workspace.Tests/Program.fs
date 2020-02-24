module DotnetMergeNupkg.Program

open Expecto
open System
open System.IO

open TestsConfig

Expecto.Expect.defaultDiffPrinter <- Expecto.Diff.colourisedDiff

type Args =
    { RunOnlyFlaky: bool
      RunOnlyKnownFailure: bool }

let rec parseArgs (config, runArgs, args) =
    match args with
    | "--flaky" :: xs ->
        parseArgs ({ config with SkipFlaky = false }, { runArgs with RunOnlyFlaky = true }, xs)
    | "--known-failure" :: xs ->
        parseArgs ({ config with SkipKnownFailure = false }, { runArgs with RunOnlyKnownFailure = true }, xs)
    | xs ->
        config, runArgs, xs

[<EntryPoint>]
let main argv =

    let suiteConfig, runArgs, otherArgs =
        let defaultConfig = { SkipFlaky = true; SkipKnownFailure = true }
        let defaultRunArgs = { Args.RunOnlyFlaky = false; RunOnlyKnownFailure = false }
        parseArgs (defaultConfig, defaultRunArgs, List.ofArray argv)

    Environment.SetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL", "1")
    Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null)

    let resultsPath = IO.Path.Combine(__SOURCE_DIRECTORY__,"..","..","bin","test_results","Workspace.TestResults.xml")

    let writeResults = TestResults.writeNUnitSummary (resultsPath, "Dotnet.ProjInfo.Workspace.Tests")
    let config = defaultConfig.appendSummaryHandler writeResults

    let tests =
        Tests.tests suiteConfig
        |> Test.filter (fun s -> if runArgs.RunOnlyFlaky then s.Contains "[flaky]" else true)
        |> Test.filter (fun s -> if runArgs.RunOnlyKnownFailure then s.Contains "[known-failure]" else true)

    Tests.runTestsWithArgs config (otherArgs |> Array.ofList) tests
