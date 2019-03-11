module DotnetMergeNupkg.Program

open Expecto
open System
open System.IO

[<EntryPoint>]
let main argv =

    Environment.SetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL", "1")
    Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null)

    let resultsPath = IO.Path.Combine(__SOURCE_DIRECTORY__,"..","..","bin","test_results","Workspace.TestResults.xml")

    let writeResults = TestResults.writeNUnitSummary (resultsPath, "Dotnet.ProjInfo.Workspace.FCS.Tests")
    let config = defaultConfig.appendSummaryHandler writeResults

    Tests.runTestsWithArgs config argv (Tests.tests ())
