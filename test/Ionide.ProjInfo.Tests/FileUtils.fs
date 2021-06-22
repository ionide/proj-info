module FileUtils

open System.IO
open Expecto.Logging
open Expecto.Logging.Message
open Expecto.Impl
open System
open System.Runtime.InteropServices

let (/) a b = Path.Combine(a, b)

let rm_rf (logger: Logger) path =
    logger.debug (eventX "rm -rf '{path}'" >> setField "path" path)

    if Directory.Exists path then
        Directory.Delete(path, true)

    if File.Exists path then
        File.Delete(path)

let mkdir_p (logger: Logger) dir =
    if not (Directory.Exists dir) then
        logger.debug (eventX "mkdir -p '{directory}'" >> setField "directory" dir)
        Directory.CreateDirectory(dir) |> ignore

let cp (logger: Logger) (from: string) (toPath: string) =
    logger.debug (eventX "cp '{from}' '{toPath}'" >> setField "from" from >> setField "toPath" toPath)

    let toFilePath =
        if (Directory.Exists(toPath)) then
            toPath / Path.GetFileName(from)
        else
            toPath

    File.Copy(from, toFilePath, true)

let cp_r (logger: Logger) (from: string) (toPath: string) =
    logger.debug (eventX "cp -r '{from}' '{toPath}'" >> setField "from" from >> setField "toPath" toPath)

    for dirPath in Directory.GetDirectories(from, "*", SearchOption.AllDirectories) do
        let newDir = dirPath.Replace(from, toPath)
        logger.debug (eventX "-> create dir '{newDir}'" >> setField "newDir" newDir)
        Directory.CreateDirectory(dirPath.Replace(from, toPath)) |> ignore

    for newPath in Directory.GetFiles(from, "*", SearchOption.AllDirectories) do
        let toFilePath = newPath.Replace(from, toPath)
        logger.debug (eventX "-> copy file '{from}' '{toPath}'" >> setField "from" newPath >> setField "toPath" toFilePath)
        File.Copy(newPath, toFilePath, true)

let shellExecRun (logger: Logger) workDir cmd (args: string list) =
    logger.debug (
        eventX "executing: '{cmd}' '{args}'"
        >> setField "cmd" cmd
        >> setField "args" (args |> String.concat " ")
        >> setField "workingDir" workDir
    )

    let cmd =
        Medallion.Shell.Command.Run(cmd, args |> Array.ofList |> Array.map box, options = (fun opt -> opt.WorkingDirectory(workDir) |> ignore))

    cmd.Wait()

    if not (String.IsNullOrWhiteSpace(cmd.Result.StandardOutput)) then
        logger.debug (eventX "output: '{output}'" >> setField "output" cmd.Result.StandardOutput)

    if not (String.IsNullOrWhiteSpace(cmd.Result.StandardError)) then
        logger.debug (eventX "error: '{error}'" >> setField "error" cmd.Result.StandardError)

    cmd

let isWindows () =
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)

let shellExecRunNET (logger: Logger) workDir cmd (args: string list) =
    if (isWindows ()) then
        shellExecRun logger workDir cmd args
    else
        shellExecRun logger workDir "mono" (cmd :: args)


let createFile (logger: Logger) path setContent =
    logger.debug (eventX "create file '{path}'" >> setField "path" path)
    use f = File.CreateText(path)
    setContent f
    ()

let unzip (logger: Logger) file dir =
    logger.debug (eventX "unzip '{file}' to {directory}" >> setField "file" file >> setField "directory" dir)
    System.IO.Compression.ZipFile.ExtractToDirectory(file, dir)

let readFile (logger: Logger) path =
    logger.debug (eventX "reading file '{path}'" >> setField "path" path)
    File.OpenRead(path)

let touch (logger: Logger) path =
    logger.debug (eventX "touch '{path}'" >> setField "path" path)
    //TODO create if not exists
    //TODO works if already in
    System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow)

type FileUtils(logger: Logger) =
    let mutable currentDirectory = Environment.CurrentDirectory

    member __.cd dir =
        logger.debug (eventX "cd '{directory}'" >> setField "directory" dir)
        currentDirectory <- dir

    member __.rm_rf = rm_rf logger
    member __.mkdir_p = mkdir_p logger
    member __.cp = cp logger
    member __.cp_r = cp_r logger
    member __.shellExecRun = shellExecRun logger currentDirectory
    member __.shellExecRunNET = shellExecRunNET logger currentDirectory
    member __.createFile = createFile logger
    member __.unzip = unzip logger
    member __.readFile = readFile logger
    member __.touch = touch logger

let writeLines (lines: string list) (stream: StreamWriter) = lines |> List.iter stream.WriteLine
