#!/usr/bin/env -S dotnet fsi --
#r "nuget: Fun.Build, 1.2.0"
#r "nuget: Ionide.KeepAChangelog, 0.2.0"

open System
open System.IO
open Fun.Build
open Ionide.KeepAChangelog

let (</>) a b = Path.Combine(a, b)
let root = __SOURCE_DIRECTORY__

let isCI = not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable "CI"))

/// Whether this run was asked not to publish anything.
let isDryRun =
    fsi.CommandLineArgs
    |> Array.contains "--dry-run"

/// The packages `dotnet build` produced, GeneratePackageOnBuild is on for every project in src.
let packages () =
    Directory.EnumerateFiles(
        root
        </> "src",
        "*.nupkg",
        SearchOption.AllDirectories
    )
    |> Seq.toList

let cleanStage =
    stage "Clean" {
        run (fun _ ->
            packages ()
            |> List.iter File.Delete
        )
    }

let buildStage = stage "Build" { run "dotnet build -c Release" }

/// Runs the tests against one runtime. The Microsoft.Build assemblies that get loaded follow the SDK,
/// so a temporary test/global.json selects the SDK that matches the target framework.
let testStage (tfm: string) (sdk: string) (buildEnvVar: string option) =
    stage $"Test %s{tfm}" {
        workingDir (
            root
            </> "test"
        )

        envVars [
            match buildEnvVar with
            | Some name -> name, "true"
            | None -> ()
        ]

        run (fun ctx ->
            async {
                let globalJson =
                    root
                    </> "test"
                    </> "global.json"

                File.WriteAllText(globalJson, $"""{{ "sdk": {{ "version": "%s{sdk}", "rollForward": "latestMinor" }} }}""")

                try
                    // The solution restore excludes dirs.proj, the only test asset that uses a NuGet-provided MSBuild SDK.
                    match! ctx.RunCommand "dotnet restore examples/traversal-project/dirs.proj" with
                    | Error _ -> return 1
                    | Ok() ->
                        let failOnFocus =
                            if isCI then
                                "Expecto.fail-on-focused-tests=true"
                            else
                                ""

                        match! ctx.RunCommand $"dotnet test --blame --blame-hang-timeout 120s --framework %s{tfm} --logger trx --logger GitHubActions -c Release Ionide.ProjInfo.Tests/Ionide.ProjInfo.Tests.fsproj -- %s{failOnFocus}" with
                        | Error _ -> return 1
                        | Ok() -> return 0
                finally
                    File.Delete globalJson
            }
        )
    }

let testNet8Stage = testStage "net8.0" "8.0.100" None
let testNet9Stage = testStage "net9.0" "9.0.100" (Some "BuildNet9")
let testNet10Stage = testStage "net10.0" "10.0.100" (Some "BuildNet10")

pipeline "Build" {
    workingDir root
    cleanStage

    stage "CheckFormat" {
        run "dotnet tool restore"
        run "dotnet fantomas --check ."
    }

    buildStage
    testNet8Stage
    testNet9Stage
    testNet10Stage
    runIfOnlySpecified false
}

pipeline "Test" {
    workingDir root
    buildStage
    testNet8Stage
    testNet9Stage
    testNet10Stage
    runIfOnlySpecified true
}

pipeline "Format" {
    workingDir root

    stage "Format" {
        run "dotnet tool restore"
        run "dotnet fantomas ."
    }

    runIfOnlySpecified true
}

/// Version and GitHub release notes of the newest release in CHANGELOG.md.
let latestChangelogRelease () =
    match
        Parser.parseChangeLog (
            FileInfo(
                root
                </> "CHANGELOG.md"
            )
        )
    with
    | Error error -> failwithf "Could not parse CHANGELOG.md: %A" error
    | Ok changelogs ->
        // The topmost entry, the same one the Release workflow's detect job reads.
        let version, _, data =
            match changelogs.Releases with
            | [] -> failwith "CHANGELOG.md has no release entry."
            | release :: _ -> release

        let notes =
            match data with
            | None -> ""
            | Some data ->
                [
                    "Added", data.Added
                    "Changed", data.Changed
                    "Fixed", data.Fixed
                    "Deprecated", data.Deprecated
                    "Removed", data.Removed
                    "Security", data.Security
                    yield! Map.toList data.Custom
                ]
                |> List.filter (fun (_, body) -> not (String.IsNullOrWhiteSpace body))
                |> List.map (fun (header, body) -> $"### %s{header}\n\n%s{body.Trim()}")
                |> String.concat "\n\n"

        string version, notes

// Publishes the latest version in CHANGELOG.md to NuGet and as a GitHub release.
// The Release workflow only runs this when that GitHub release does not exist yet.
// `dotnet fsi build.fsx -- -p Release --dry-run` only prints what would happen.
pipeline "Release" {
    workingDir root
    cleanStage
    buildStage

    stage "Release" {
        run (fun ctx ->
            async {
                let version, notes = latestChangelogRelease ()
                let tag = $"v%s{version}"

                // The workflow reads the version with sed, guard against it disagreeing with the parser.
                match Environment.GetEnvironmentVariable "RELEASE_VERSION" with
                | expected when
                    not (String.IsNullOrWhiteSpace expected)
                    && expected
                       <> version
                    ->
                    failwithf "The workflow detected version %s, but CHANGELOG.md parses as %s." expected version
                | _ -> ()

                let pkgs = packages ()

                match
                    pkgs
                    |> List.filter (fun pkg -> not (pkg.EndsWith($".%s{version}.nupkg", StringComparison.Ordinal)))
                with
                | [] when not pkgs.IsEmpty -> ()
                | [] -> failwith "No packages found."
                | unexpected -> failwithf "Packages do not match changelog version %s: %A" version unexpected

                let notesFile = Path.GetTempFileName()
                File.WriteAllText(notesFile, notes)

                let assets =
                    pkgs
                    |> List.map (sprintf "\"%s\"")
                    |> String.concat " "

                let target =
                    match Environment.GetEnvironmentVariable "GITHUB_SHA" with
                    | sha when not (String.IsNullOrWhiteSpace sha) -> $" --target %s{sha}"
                    | _ -> ""

                let ghArgs = $"release create %s{tag} %s{assets} --title %s{tag} --notes-file \"%s{notesFile}\"%s{target}"

                try
                    if isDryRun then
                        printfn $"[dry-run] Release notes for %s{tag}:\n%s{notes}"

                        for pkg in pkgs do
                            printfn $"[dry-run] dotnet nuget push %s{pkg} --skip-duplicate"

                        printfn $"[dry-run] gh %s{ghArgs}"
                        return 0
                    else
                        let key = Environment.GetEnvironmentVariable "NUGET_KEY"

                        if String.IsNullOrWhiteSpace key then
                            failwith "NUGET_KEY is not set."

                        let rec push pkgs =
                            async {
                                match pkgs with
                                | [] -> return Ok()
                                | pkg :: rest ->
                                    match! ctx.RunSensitiveCommand $"dotnet nuget push \"{pkg}\" --api-key {key} --source https://api.nuget.org/v3/index.json --skip-duplicate" with
                                    | Error e -> return Error e
                                    | Ok() -> return! push rest
                            }

                        match! push pkgs with
                        | Error _ -> return 1
                        | Ok() ->
                            match! ctx.RunCommand $"gh %s{ghArgs}" with
                            | Error _ -> return 1
                            | Ok() -> return 0
                finally
                    File.Delete notesFile
            }
        )
    }

    runIfOnlySpecified true
}

tryPrintPipelineCommandHelp ()
