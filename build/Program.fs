open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Core.TargetOperators

// --------------------------------------------------------------------------------------
// Information about the project to be used at NuGet and in AssemblyInfo files
// --------------------------------------------------------------------------------------

let summary = "MsBuild evaluation, fsproj file loading, and project system for F# tooling"

let gitOwner = "ionide"
let gitName = "proj-info"
let gitHome = "https://github.com/" + gitOwner
let gitUrl = gitHome + "/" + gitName

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

let nugetDir = "./out/"

System.Environment.CurrentDirectory <- (Path.combine __SOURCE_DIRECTORY__ "..")

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

let initializeContext args =
    let execContext = Context.FakeExecutionContext.Create false "build.fsx" args
    Context.setExecutionContext (Context.RuntimeContext.Fake execContext)

let getBuildParam = Environment.environVar
let DoNothing = ignore

let init args =
    initializeContext args

    let testNet7 =
        match System.Environment.GetEnvironmentVariable("BuildNet7") |> bool.TryParse with
        | true, v -> v
        | _ -> false

    Target.create "Clean" (fun _ -> Shell.cleanDirs [ nugetDir ])

    Target.create "Build" (fun _ -> DotNet.build id "")

    let testTFM tfm =
        exec "dotnet" $"run --no-build --framework {tfm} --project .\\test\\Ionide.ProjInfo.Tests\\Ionide.ProjInfo.Tests.fsproj" "."
        |> ignore

    Target.create "Test" DoNothing

    Target.create "Test:net6.0" (fun _ -> testTFM "net6.0")
    Target.create "Test:net7.0" (fun _ -> testTFM "net7.0")

    "Build" ?=> "Test:net6.0" =?> ("Test", not testNet7)
    "Build" ?=> "Test:net7.0" =?> ("Test", testNet7)

    Target.create "Pack" (fun _ ->
        DotNet.pack
            (fun p ->
                { p with
                    Configuration = DotNet.BuildConfiguration.Release
                    OutputPath = Some nugetDir })
            "ionide-proj-info.sln")

    Target.create "Push" (fun _ ->
        let key =
            match getBuildParam "nuget-key" with
            | s when not (isNullOrWhiteSpace s) -> s
            | _ -> UserInput.getUserPassword "NuGet Key: "

        Paket.push (fun p ->
            { p with
                WorkingDir = nugetDir
                ApiKey = key
                ToolType = ToolType.CreateLocalTool() }))


    let sourceFiles = !! "src/**/*.fs" ++ "build.fsx" -- "src/**/obj/**/*.fs"

    Target.create "CheckFormat" (fun _ ->
        let result =
            sourceFiles |> Seq.map (sprintf "\"%s\"") |> String.concat " " |> sprintf "%s --check" |> DotNet.exec id "fantomas"

        if result.ExitCode = 0 then
            Trace.log "No files need formatting"
        elif result.ExitCode = 99 then
            failwith "Some files need formatting, check output for more info"
        else
            Trace.logf "Errors while formatting: %A" result.Errors)

    Target.create "Format" (fun _ ->
        let result = sourceFiles |> Seq.map (sprintf "\"%s\"") |> String.concat " " |> DotNet.exec id "fantomas"

        if not result.OK then
            printfn "Errors while formatting all files: %A" result.Messages)

    Target.create "Default" DoNothing

    Target.create "Release" DoNothing

    let dependencies = "Clean" ==> "CheckFormat" ==> "Build" ==> "Test" ==> "Default" ==> "Pack"
    ()

[<EntryPoint>]
let main args =
    init ((args |> List.ofArray))

    try
        match args with
        | [| target |] -> Target.runOrDefaultWithArguments target
        | _ -> Target.runOrDefaultWithArguments "Default"

        0
    with
    | e ->
        printfn "%A" e
        1
