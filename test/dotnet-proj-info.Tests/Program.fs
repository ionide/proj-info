module DotnetMergeNupkg.Program

open Expecto

[<EntryPoint>]
let main argv =
    match argv |> List.ofArray with
    | [] ->
        printfn "expected packageversion as first argument"
        1
    | pkgUnderTestVersion :: args ->
        printfn "testing package: %s" pkgUnderTestVersion
        Tests.runTestsWithArgs defaultConfig (args |> Array.ofList) (DotnetProjInfo.Tests.tests pkgUnderTestVersion)
