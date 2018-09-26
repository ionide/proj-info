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

        Tests.runTestsWithArgs defaultConfig (args |> Array.ofList) (DotnetProjInfo.Tests.tests pkgUnderTestVersion)
