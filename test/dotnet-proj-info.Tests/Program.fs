module DotnetMergeNupkg.Program

open Expecto
open System
open System.IO

[<EntryPoint>]
let main argv =
    let artifactsDir =
        IO.Path.Combine(__SOURCE_DIRECTORY__,"..","..","bin")
        |> Path.GetFullPath
    let nupkgsDir = IO.Path.Combine(artifactsDir,"nupkg")

    let findPackedVersion () =
        if not (Directory.Exists nupkgsDir) then
            None
        else
            nupkgsDir
            |> Directory.EnumerateFiles
            |> Seq.map Path.GetFileNameWithoutExtension
            |> Seq.tryFind (fun p -> p.StartsWith("dotnet-proj-info"))
            |> Option.map (fun p -> p.Replace("dotnet-proj-info.",""))

    let info =
        match argv |> List.ofArray with
        | pkgUnderTestVersion :: args when Char.IsNumber(pkgUnderTestVersion.[0]) ->
            printfn "testing package: %s" pkgUnderTestVersion
            Ok (pkgUnderTestVersion, args)
        | args ->
            printfn "Package version not passed as first argument, searching in nupks dir"
            match findPackedVersion () with
            | Some v ->
                printfn "found version '%s' of dotnet-proj-info" v
                Ok (v, args)
            | None ->
                printfn "dotnet-proj-info nupkg not found in '%s'" nupkgsDir
                Error 1

    match info with
    | Error exitCode ->
        printfn "expected package version as first argument, or dotnet-proj-info nupkg in dir '%s'" nupkgsDir
        exitCode
    | Ok (pkgUnderTestVersion, args) ->
        printfn "testing package: %s" pkgUnderTestVersion

        Environment.SetEnvironmentVariable("DOTNET_PROJ_INFO_MSBUILD_BL", "1")
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", null)

        let resultsPath = IO.Path.Combine(__SOURCE_DIRECTORY__,"..","..","bin","test_results","TestResults.xml")

        let writeResults = TestResults.writeNUnitSummary (resultsPath, "dotnet-proj-info.Tests")
        let config = defaultConfig.appendSummaryHandler writeResults

        Tests.runTestsWithArgs config (args |> Array.ofList) (DotnetProjInfo.Tests.tests pkgUnderTestVersion)
