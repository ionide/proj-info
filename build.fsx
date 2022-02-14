// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#r "paket: groupref build //"

#load ".fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let summary =
    "MsBuild evaluation, fsproj file loading, and project system for F# tooling"

let authors = "enricosada; Krzysztof-Cieslak;"
let tags = "msbuild;dotnet;sdk;fsproj"

let gitOwner = "ionide"
let gitName = "dotnet-proj-info"
let gitHome = "https://github.com/" + gitOwner
let gitUrl = gitHome + "/" + gitName

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

let buildDir = "./build/"

let nugetDir = "./out/"


System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------
let isNullOrWhiteSpace = System.String.IsNullOrWhiteSpace

let exec cmd args dir =
    let proc =
        CreateProcess.fromRawCommandLine cmd args
        |> CreateProcess.ensureExitCodeWithMessage (sprintf "Error while running '%s' with args: %s" cmd args)

    (if isNullOrWhiteSpace dir then
         proc
     else
         proc |> CreateProcess.withWorkingDirectory dir)
    |> Proc.run
    |> ignore

let getBuildParam = Environment.environVar
let DoNothing = ignore
// --------------------------------------------------------------------------------------
// Build Targets
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ -> Shell.cleanDirs [ buildDir; nugetDir ])

Target.create "ReplaceFsLibLogNamespaces"
<| fun _ ->
    let replacements =
        [ "FsLibLog\\n", "Ionide.ProjInfo.Logging\n"
          "FsLibLog\\.", "Ionide.ProjInfo.Logging" ]

    replacements
    |> List.iter
        (fun (``match``, replace) ->
            (!! "paket-files/TheAngryByrd/FsLibLog/**/FsLibLog*.fs")
            |> Shell.regexReplaceInFilesWithEncoding ``match`` replace System.Text.Encoding.UTF8)

Target.create "Build" (fun _ -> DotNet.build id "")

Target.create
    "Test"
    (fun _ -> exec "dotnet" @"run --project .\test\Ionide.ProjInfo.Tests\Ionide.ProjInfo.Tests.fsproj" ".")

Target.create
    "BuildRelease"
    (fun _ ->
        DotNet.build
            (fun p ->
                { p with
                      Configuration = DotNet.BuildConfiguration.Release
                      OutputPath = Some buildDir })
            "ionide-proj-info.sln")

// --------------------------------------------------------------------------------------
// Release Targets
// --------------------------------------------------------------------------------------

Target.create
    "Pack"
    (fun _ ->
        let properties =
            [ ("Authors", authors)
              ("PackageProjectUrl", gitUrl)
              ("PackageTags", tags)
              ("RepositoryType", "git")
              ("RepositoryUrl", gitUrl)
              ("PackageLicenseExpression", "MIT")
              ("PackageDescription", summary)
              ("EnableSourceLink", "true") ]


        DotNet.pack
            (fun p ->
                { p with
                      Configuration = DotNet.BuildConfiguration.Release
                      OutputPath = Some nugetDir
                      MSBuildParams =
                          { p.MSBuildParams with
                                Properties = properties } })
            "ionide-proj-info.sln")

Target.create
    "Push"
    (fun _ ->
        let key =
            match getBuildParam "nuget-key" with
            | s when not (isNullOrWhiteSpace s) -> s
            | _ -> UserInput.getUserPassword "NuGet Key: "

        Paket.push
            (fun p ->
                { p with
                      WorkingDir = nugetDir
                      ApiKey = key
                      ToolType = ToolType.CreateLocalTool() }))


// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------
Target.create "Default" DoNothing

Target.create "Release" DoNothing

"Clean"
==> "ReplaceFsLibLogNamespaces"
==> "Build"
==> "Test"
==> "Default"
==> "Pack"

Target.runOrDefault "Default"
