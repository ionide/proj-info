module DotnetMergeNupkg.Program

open Expecto
open System

[<EntryPoint>]
let main argv =
    match argv |> List.ofArray with
    | [] ->
        printfn "expected packageversion as first argument"
        1
    | pkgUnderTestVersion :: args ->
        printfn "testing package: %s" pkgUnderTestVersion

        Environment.SetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL", "1")
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null)

        let resultsPath = IO.Path.Combine(__SOURCE_DIRECTORY__,"..","..","bin","test_results","TestResults.xml")

        let writeResults = TestResults.writeNUnitSummary (resultsPath, "dotnet-proj-info.Tests")
        let config = defaultConfig.appendSummaryHandler writeResults

        Tests.runTestsWithArgs config (args |> Array.ofList) (DotnetProjInfo.Tests.tests pkgUnderTestVersion)
