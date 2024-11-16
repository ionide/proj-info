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

let init args =
    initializeContext args


    let ignoreTests =
        match
            System.Environment.GetEnvironmentVariable("IgnoreTests")
            |> bool.TryParse
        with
        | true, v -> v
        | _ -> false

    let packages () = !! "src/**/*.nupkg"

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
    }

    Target.create "Build" (fun _ -> DotNet.build buildOpts "")

    let tfmToSdkMap =
        Map.ofSeq [
            "net8.0", "8.0.100"
            "net9.0", "9.0.100"
        ]

    let tfmToBuildNet9Map =
        Map.ofSeq [
            "net8.0", false
            "net9.0", true
        ]

    let testTFM tfm =
        try
            exec "dotnet" $"new globaljson --force --sdk-version {tfmToSdkMap.[tfm]} --roll-forward LatestMinor" "test" Map.empty

            exec
                "dotnet"
                $"test --blame --blame-hang-timeout 60s --framework {tfm} --logger trx --logger GitHubActions -c Release .\\Ionide.ProjInfo.Tests\\Ionide.ProjInfo.Tests.fsproj"
                "test"
                (Map.ofSeq [ "BuildNet9", tfmToBuildNet9Map.[tfm].ToString() ])
            |> ignore
        finally
            System.IO.File.Delete "test\\global.json"

    Target.create "Test" DoNothing

    Target.create "Test:net8.0" (fun _ -> testTFM "net8.0")
    Target.create "Test:net9.0" (fun _ -> testTFM "net9.0")

    "Build"
    ==> ("Test:net8.0")
    =?> ("Test", not ignoreTests)
    |> ignore

    "Build"
    ==> ("Test:net9.0")
    =?> ("Test", not ignoreTests)
    |> ignore

    Target.create
        "ListPackages"
        (fun _ ->
            packages ()
            |> Seq.iter (fun pkg -> printfn $"Found package at: {pkg}")
        )

    Target.create
        "Push"
        (fun _ ->
            let key =
                match getBuildParam "nuget-key" with
                | s when not (isNullOrWhiteSpace s) -> s
                | _ -> UserInput.getUserPassword "NuGet Key: "

            let pushPkg (opts: DotNet.NuGetPushOptions) = {
                opts with
                    PushParams = {
                        opts.PushParams with
                            ApiKey = Some key
                            Source = Some "https://api.nuget.org/v3/index.json"
                    }
            }

            packages ()
            |> Seq.iter (fun pkg -> DotNet.nugetPush pushPkg pkg)
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

    Target.create "Release" DoNothing

    "Clean"
    ==> "CheckFormat"
    ==> "Build"
    ==> "Test"
    ==> "Default"
    |> ignore

[<EntryPoint>]
let main args =
    init (
        (args
         |> List.ofArray)
    )

    try
        match args with
        | [| target |] -> Target.runOrDefaultWithArguments target
        | _ -> Target.runOrDefaultWithArguments "Default"

        0
    with e ->
        printfn "%A" e
        1
