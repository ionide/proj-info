namespace Ionide.ProjInfo.Tests

module TestUtils =
    open DotnetProjInfo.TestAssets
    open Expecto
    open Expecto.Logging
    open Expecto.Logging.Message
    open FileUtils
    open FSharp.Compiler.CodeAnalysis
    open Ionide.ProjInfo
    open Ionide.ProjInfo.Types
    open Medallion.Shell
    open System
    open System.Collections.Generic
    open System.IO
    open System.Threading
    open System.Xml.Linq
    open System.Linq

    module Exception =
        open System.Runtime.ExceptionServices

        let inline reraiseAny (e: exn) =
            ExceptionDispatchInfo.Capture(e).Throw()


    let RepoDir =
        (__SOURCE_DIRECTORY__
         / ".."
         / "..")
        |> Path.GetFullPath


    let ExamplesDir =
        RepoDir
        / "test"
        / "examples"


    let normalizeFileName (fileName: string) =
        if String.IsNullOrEmpty fileName then
            ""
        else
            let invalidChars = HashSet<char>(Path.GetInvalidFileNameChars())
            let chars = fileName.AsSpan()
            let mutable output = Span<char>.Empty
            let mutable outputIndex = 0
            let mutable lastWasUnderscore = false

            // Use a fixed-size buffer (stack-alloc if small enough)
            let buffer = Span<char>(Array.zeroCreate<char> (min fileName.Length 255))
            output <- buffer

            for i = 0 to chars.Length
                         - 1 do
                let c = chars.[i]

                if outputIndex < 255 then
                    if
                        invalidChars.Contains(c)
                        || Char.IsControl(c)
                    then
                        if
                            not lastWasUnderscore
                            && outputIndex > 0
                        then
                            output.[outputIndex] <- '_'

                            outputIndex <-
                                outputIndex
                                + 1

                            lastWasUnderscore <- true
                    else
                        output.[outputIndex] <- c

                        outputIndex <-
                            outputIndex
                            + 1

                        lastWasUnderscore <- false

            // Trim leading/trailing underscores
            let start =
                if
                    outputIndex > 0
                    && output.[0] = '_'
                then
                    1
                else
                    0

            let length =
                if
                    outputIndex > 0
                    && output.[outputIndex
                               - 1] = '_'
                then
                    outputIndex
                    - start
                    - 1
                else
                    outputIndex
                    - start

            if
                length
                <= 0
            then
                ""
            else
                output.Slice(start, length).ToString()

    let pathForTestAssets (test: TestAssetProjInfo) =
        ExamplesDir
        / test.ProjDir

    let pathForProject (test: TestAssetProjInfo) =
        pathForTestAssets test
        / test.ProjectFile

    let implAssemblyForProject (test: TestAssetProjInfo) = $"{test.AssemblyName}.dll"

    let refAssemblyForProject (test: TestAssetProjInfo) =
        Path.Combine("ref", implAssemblyForProject test)

    let getResult (r: Result<_, _>) =
        match r with
        | Ok x -> x
        | Result.Error e -> failwithf "%A" e

    let TestRunDir =
        RepoDir
        / "test"
        / "testrun_ws"

    let TestRunInvariantDir =
        TestRunDir
        / "invariant"


    let checkExitCodeZero (cmd: Command) =
        Expect.equal 0 cmd.Result.ExitCode $"command {cmd.Result.StandardOutput} finished with exit code non-zero."

    let findByPath path parsed =
        parsed
        |> Array.tryPick (fun (kv: KeyValuePair<string, ProjectOptions>) ->
            if kv.Key = path then
                Some kv
            else
                None
        )
        |> function
            | Some x -> x
            | None ->
                failwithf
                    "key '%s' not found in %A"
                    path
                    (parsed
                     |> Array.map (fun kv -> kv.Key))

    let expectFind projPath msg (parsed: ProjectOptions list) =
        let p =
            parsed
            |> List.tryFind (fun n -> n.ProjectFileName = projPath)

        Expect.isSome p msg
        p.Value


    let inDir (fs: FileUtils) dirName =
        let outDir =
            TestRunDir
            / dirName

        fs.rm_rf outDir
        fs.mkdir_p outDir
        fs.cd outDir
        outDir

    let copyDirFromAssets (fs: FileUtils) source outDir =
        fs.mkdir_p outDir

        let path =
            ExamplesDir
            / source

        fs.cp_r path outDir
        ()

    let dotnet (fs: FileUtils) args = fs.shellExecRun "dotnet" args

    let withLog name f test =
        test
            name
            (fun () ->

                let logger = Log.create (sprintf "Test '%s'" name)
                let fs = FileUtils(logger)
                f logger fs
            )

    let renderOf sampleProj sources = {
        ProjectViewerTree.Name =
            sampleProj.ProjectFile
            |> Path.GetFileNameWithoutExtension
        Items =
            sources
            |> List.map (fun (path, link) -> ProjectViewerItem.Compile(path, { ProjectViewerItemConfig.Link = link }))
    }

    let createFCS () =
        let checker = FSharpChecker.Create(projectCacheSize = 200, keepAllBackgroundResolutions = true, keepAssemblyContents = true)
        checker

    let sleepABit () =
        // CI has apparent occasional slowness
        System.Threading.Thread.Sleep 5000
