module FileUtils

open System.IO
open System
open System.Runtime.InteropServices
open Microsoft.Extensions.Logging

let (/) a b = Path.Combine(a, b)

let isWindows () =
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

let writeLines (lines: string list) (stream: StreamWriter) =
    lines
    |> List.iter stream.WriteLine

type FileUtils(logger: ILogger) =
    let mutable currentDirectory = Environment.CurrentDirectory

    member _.rm_rf (path: string) =
        logger.LogDebug ("rm -rf '{path}'", path)

        if Directory.Exists path then
            Directory.Delete(path, true)

        if File.Exists path then
            File.Delete(path)

    member _.mkdir_p dir =
        if not (Directory.Exists dir) then
            logger.LogDebug ("mkdir -p '{directory}'", dir)

            Directory.CreateDirectory(dir)
            |> ignore

    member _.cp  (from: string) (toPath: string) =
        logger.LogDebug ("cp '{from}' '{toPath}'", from, toPath)
        let toFilePath =
            if (Directory.Exists(toPath)) then
                toPath
                / Path.GetFileName(from)
            else
                toPath

        File.Copy(from, toFilePath, true)

    member _.cp_r (from: string) (toPath: string) =
        logger.LogDebug ("cp -r '{from}' '{toPath}'", from, toPath)

        for dirPath in Directory.GetDirectories(from, "*", SearchOption.AllDirectories) do
            let newDir = dirPath.Replace(from, toPath)

            logger.LogDebug ("-> create dir '{newDir}'", newDir)

            Directory.CreateDirectory(dirPath.Replace(from, toPath))
            |> ignore

        for newPath in Directory.GetFiles(from, "*", SearchOption.AllDirectories) do
            let toFilePath = newPath.Replace(from, toPath)
            logger.LogDebug ("-> copy file '{from}' '{toPath}'", newPath, toFilePath)
            File.Copy(newPath, toFilePath, true)

    member _.shellExecRun cmd (args: string list) =
        logger.LogDebug ("executing: '{cmd}' '{args}'", cmd, (args |> String.concat " "), currentDirectory)

        let cmd =
            Medallion.Shell.Command.Run(
                cmd,
                args
                |> Array.ofList
                |> Array.map box,
                options =
                    (fun opt ->
                        opt.WorkingDirectory(currentDirectory)
                        |> ignore
                    )
            )

        cmd.Wait()

        if not (String.IsNullOrWhiteSpace(cmd.Result.StandardOutput)) then
            logger.LogDebug ("output: '{output}'", cmd.Result.StandardOutput)

        if not (String.IsNullOrWhiteSpace(cmd.Result.StandardError)) then
            logger.LogDebug ("error: '{error}'", cmd.Result.StandardError)

        cmd

    member _.cd (dir: string) =
        logger.LogDebug ("cd '{directory}'", dir)
        currentDirectory <- dir

    member x.shellExecRunNET cmd (args: string list) =
        if (isWindows ()) then
            x.shellExecRun cmd args
        else
            x.shellExecRun
                "mono"
                (cmd
                :: args)


    member _.createFile (path: string) setContent =
        logger.LogDebug ("create file '{path}'", path)

        use f = File.CreateText(path)
        setContent f
        ()

    member _.unzip (file: string) (dir: string) =
        logger.LogDebug ("unzip '{file}' to {directory}", file, dir)

        System.IO.Compression.ZipFile.ExtractToDirectory(file, dir)

    member _.readFile (path: string) =
        logger.LogDebug ("reading file '{path}'", path)

        File.OpenRead(path)

    member _.touch (path: string) =
        logger.LogDebug ("touch '{path}'", path)
        //TODO create if not exists
        //TODO works if already in
        System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow)
