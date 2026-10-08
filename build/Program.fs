open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

System.Environment.CurrentDirectory <- (Path.combine __SOURCE_DIRECTORY__ "..")

// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------
let isNullOrWhiteSpace = System.String.IsNullOrWhiteSpace

let environVarAsBoolOrDefault varName defaultValue =
    let truthyConsts = [
        "1"
        "Y"
        "YES"
        "T"
        "TRUE"
    ]

    try
        let envvar = (Environment.environVar varName).ToUpper()

        truthyConsts
        |> List.exists ((=) envvar)
    with _ ->
        defaultValue


let isCI = lazy (environVarAsBoolOrDefault "CI" false)

let exec cmd args dir env =
    let proc =
        CreateProcess.fromRawCommandLine cmd args
        |> CreateProcess.ensureExitCodeWithMessage (sprintf "Error while running '%s' with args: %s" cmd args)
        |> CreateProcess.withEnvironment (Map.toList env)

    (if isNullOrWhiteSpace dir then
         proc
     else
         proc
         |> CreateProcess.withWorkingDirectory dir)
    |> Proc.run
    |> ignore

let initializeContext args =
    let execContext = Context.FakeExecutionContext.Create false "build.fsx" args
    Context.setExecutionContext (Context.RuntimeContext.Fake execContext)

let getBuildParam = Environment.environVar
let DoNothing = ignore


/// Version and GitHub release notes of the newest release in CHANGELOG.md.
let latestChangelogRelease () =
    match Ionide.KeepAChangelog.Parser.parseChangeLog (System.IO.FileInfo "CHANGELOG.md") with
    | Error error -> failwithf "Could not parse CHANGELOG.md: %A" error
    | Ok changelogs ->
        let version, _, data =
            changelogs.Releases
            |> List.maxBy (fun (_, date, _) -> date)

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
                |> List.filter (fun (_, body) -> not (isNullOrWhiteSpace body))
                |> List.map (fun (header, body) -> $"### %s{header}\n\n%s{body.Trim()}")
                |> String.concat "\n\n"

        string version, notes

let init args =
    initializeContext args

    let configuration =
        match getBuildParam "CONFIGURATION" with
        | s when not (isNullOrWhiteSpace s) -> s
        | _ -> "Release"

    let ignoreTests =
        match
            System.Environment.GetEnvironmentVariable("IgnoreTests")
            |> bool.TryParse
        with
        | true, v -> v
        | _ -> false

    let packages () = !!"src/**/*.nupkg"

    Target.create
        "Clean"
        (fun _ ->
            packages ()
            |> Seq.iter Shell.rm
        )

    // Avoid msbuild loading issues: https://github.com/fsprojects/FAKE/issues/2515#issue-622856864
    let buildOpts (opts: DotNet.BuildOptions) = {
        opts with
            MSBuildParams = {
                opts.MSBuildParams with
                    DisableInternalBinLog = true
            }
            Configuration = DotNet.BuildConfiguration.fromString configuration
    }

    Target.create "Build" (fun _ -> DotNet.build buildOpts "")

    let tfmToSdkMap =
        Map.ofSeq [
            "net8.0", "8.0.100"
            "net9.0", "9.0.100"
            "net10.0", "10.0.100"
        ]

    let tfmToEnvVar =
        Map.ofSeq [
            "net8.0", None
            "net9.0", Some "BuildNet9"
            "net10.0", Some "BuildNet10"
        ]

    let testTFM tfm =
        try
            exec "dotnet" $"new globaljson --force --sdk-version {tfmToSdkMap.[tfm]} --roll-forward LatestMinor" "test" Map.empty
            // The solution restore excludes dirs.proj, the only test asset that uses a NuGet-provided MSBuild SDK.
            exec "dotnet" "restore .\\examples\\traversal-project\\dirs.proj" "test" Map.empty

            let failedOnFocus =
                if isCI.Value then
                    "Expecto.fail-on-focused-tests=true"
                else
                    ""

            let envs =
                match
                    tfmToEnvVar
                    |> Map.tryFind tfm
                    |> Option.flatten
                with
                | Some envVar -> Map.ofSeq [ envVar, "true" ]
                | None -> Map.empty

            exec "dotnet" $"test --blame --blame-hang-timeout 120s --framework {tfm} --logger trx --logger GitHubActions -c %s{configuration} .\\Ionide.ProjInfo.Tests\\Ionide.ProjInfo.Tests.fsproj -- %s{failedOnFocus}" "test" envs
            |> ignore
        finally
            System.IO.File.Delete "test\\global.json"

    Target.create "Test" DoNothing

    Target.create "Test:net8.0" (fun _ -> testTFM "net8.0")
    Target.create "Test:net9.0" (fun _ -> testTFM "net9.0")
    Target.create "Test:net10.0" (fun _ -> testTFM "net10.0")

    "Build"
    ==> ("Test:net8.0")
    =?> ("Test", not ignoreTests)
    |> ignore

    "Build"
    ==> ("Test:net9.0")
    =?> ("Test", not ignoreTests)
    |> ignore

    "Build"
    ==> ("Test:net10.0")
    =?> ("Test", not ignoreTests)
    |> ignore

    Target.create
        "ListPackages"
        (fun _ ->
            packages ()
            |> Seq.iter (fun pkg -> printfn $"Found package at: {pkg}")
        )

    // Publishes the latest version in CHANGELOG.md to NuGet and as a GitHub release.
    // The Release workflow only runs this when that GitHub release does not exist yet.
    // Pass `--dry-run` after the target to only print what would happen:
    // dotnet run --project build -- -t Release --dry-run
    Target.create
        "Release"
        (fun p ->
            let dryRun =
                p.Context.Arguments
                |> List.contains "--dry-run"

            let version, notes = latestChangelogRelease ()
            let tag = $"v%s{version}"

            // The workflow reads the version with sed, guard against it disagreeing with the parser.
            match getBuildParam "RELEASE_VERSION" with
            | expected when
                not (isNullOrWhiteSpace expected)
                && expected
                   <> version
                ->
                failwithf "The workflow detected version %s, but CHANGELOG.md parses as %s." expected version
            | _ -> ()

            let pkgs =
                packages ()
                |> Seq.toList

            let expectedSuffix = $".%s{version}.nupkg"

            match
                pkgs
                |> List.filter (fun pkg -> not (pkg.EndsWith(expectedSuffix, System.StringComparison.Ordinal)))
            with
            | [] when not pkgs.IsEmpty -> ()
            | [] -> failwith "No packages found, run the Build target first."
            | unexpected -> failwithf "Packages do not match changelog version %s: %A" version unexpected

            let notesFile = System.IO.Path.GetTempFileName()
            System.IO.File.WriteAllText(notesFile, notes)

            try
                if dryRun then
                    Trace.log $"Release notes for %s{tag}:\n%s{notes}"

                let key =
                    match getBuildParam "NUGET_KEY" with
                    | s when not (isNullOrWhiteSpace s) -> s
                    | _ when dryRun -> "dry-run-key"
                    | _ -> failwith "NUGET_KEY is not set."

                TraceSecrets.register "<NUGET_KEY>" key

                for pkg in pkgs do
                    if dryRun then
                        Trace.log $"[dry-run] dotnet nuget push %s{pkg} --skip-duplicate"
                    else
                        // Not `exec`: its failure message would contain the API key.
                        CreateProcess.fromRawCommandLine "dotnet" $"nuget push \"%s{pkg}\" --api-key %s{key} --source https://api.nuget.org/v3/index.json --skip-duplicate"
                        |> CreateProcess.ensureExitCodeWithMessage $"Failed to push %s{pkg}"
                        |> Proc.run
                        |> ignore

                let target =
                    match getBuildParam "GITHUB_SHA" with
                    | s when not (isNullOrWhiteSpace s) -> $" --target %s{s}"
                    | _ -> ""

                let assets =
                    pkgs
                    |> List.map (sprintf "\"%s\"")
                    |> String.concat " "

                let ghArgs = $"release create %s{tag} %s{assets} --title %s{tag} --notes-file \"%s{notesFile}\"%s{target}"

                if dryRun then
                    Trace.log $"[dry-run] gh %s{ghArgs}"
                else
                    exec "gh" ghArgs "" Map.empty
            finally
                System.IO.File.Delete notesFile
        )

    Target.create
        "CheckFormat"
        (fun _ ->
            let result = DotNet.exec id "fantomas" "--check ."

            if result.ExitCode = 0 then
                Trace.log "No files need formatting"
            elif result.ExitCode = 99 then
                failwith "Some files need formatting, check output for more info"
            else
                Trace.logf "Errors while formatting: %A" result.Errors
        )

    Target.create
        "Format"
        (fun _ ->
            let result = DotNet.exec id "fantomas" "."

            if not result.OK then
                printfn "Errors while formatting all files: %A" result.Messages
        )

    Target.create "Default" DoNothing

    "Clean"
    ==> "CheckFormat"
    ==> "Build"
    ==> "Test"
    ==> "Default"
    |> ignore

    "Build"
    ==> "Release"
    |> ignore

[<EntryPoint>]
let main args =
    List.ofArray args
    |> init

    try
        Target.runOrDefaultWithArguments "Default"
        0
    with e ->
        eprintfn "%A" e
        1
