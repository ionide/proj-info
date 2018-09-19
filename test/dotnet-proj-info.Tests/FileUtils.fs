module FileUtils

open System.IO
open Expecto.Logging
open Expecto.Logging.Message
open Expecto.Impl
open System

let (/) a b = Path.Combine(a, b)

let rm_rf (logger: Logger) dir =
    logger.info(
      eventX "rm -rf '{directory}'"
      >> setField "directory" dir)
    if Directory.Exists dir then
      Directory.Delete(dir, true)

let mkdir_p (logger: Logger) dir =
    if not(Directory.Exists dir) then
      logger.info(
        eventX "mkdir -p '{directory}'"
        >> setField "directory" dir)
      Directory.CreateDirectory(dir) |> ignore

let cp (logger: Logger) (from: string) (toPath: string) =
    logger.info(
      eventX "cp '{from}' '{toPath}'"
      >> setField "from" from
      >> setField "toPath" toPath)
    let toFilePath =
        if (Directory.Exists(toPath)) then
            toPath / Path.GetFileName(from)
        else
            toPath
    File.Copy(from, toFilePath, true)

let cp_r (logger: Logger) (from: string) (toPath: string) =
    logger.info(
      eventX "cp -r '{from}' '{toPath}'"
      >> setField "from" from
      >> setField "toPath" toPath)

    for dirPath in Directory.GetDirectories(from, "*", SearchOption.AllDirectories) do
        let newDir = dirPath.Replace(from, toPath)
        logger.info(
          eventX "-> create dir '{newDir}'"
          >> setField "newDir" newDir)
        Directory.CreateDirectory(dirPath.Replace(from, toPath)) |> ignore

    for newPath in Directory.GetFiles(from, "*", SearchOption.AllDirectories) do
        let toFilePath = newPath.Replace(from, toPath)
        logger.info(
          eventX "-> copy file '{from}' '{toPath}'"
          >> setField "from" newPath
          >> setField "toPath" toFilePath)
        File.Copy(newPath, toFilePath, true)

let shellExecRun (logger: Logger) workDir cmd (args: string list) =
    logger.info(
      eventX "executing: '{cmd}' '{args}'"
      >> setField "cmd" cmd
      >> setField "args" (args |> String.concat " ")
      >> setField "workingDir" workDir)
    let cmd = Medallion.Shell.Command.Run(cmd, args |> Array.ofList |> Array.map box, options = (fun opt -> opt.WorkingDirectory(workDir) |> ignore))
    cmd.Wait()
    if not (String.IsNullOrWhiteSpace(cmd.Result.StandardOutput)) then
      logger.info(
        eventX "output: '{output}'"
        >> setField "output" cmd.Result.StandardOutput)
    if not (String.IsNullOrWhiteSpace(cmd.Result.StandardError)) then
      logger.info(
        eventX "error: '{error}'"
        >> setField "error" cmd.Result.StandardError)
    cmd

let createFile (logger: Logger) path setContent =
    logger.info(
      eventX "create file '{path}'"
      >> setField "path" path)
    use f = File.CreateText(path)
    setContent f
    ()

let unzip (logger: Logger) file dir =
    logger.info(
      eventX "unzip '{file}' to {directory}"
      >> setField "file" file
      >> setField "directory" dir)
    System.IO.Compression.ZipFile.ExtractToDirectory(file, dir)

let readFile (logger: Logger) path =
    logger.info(
      eventX "reading file '{path}'"
      >> setField "path" path)
    File.OpenRead(path)

type FileUtils (logger: Logger) =
    let mutable currentDirectory = Environment.CurrentDirectory

    member __.cd dir =
        logger.info(
          eventX "cd '{directory}'"
          >> setField "directory" dir)
        currentDirectory <- dir
    member __.rm_rf = rm_rf logger
    member __.mkdir_p = mkdir_p logger
    member __.cp = cp logger
    member __.cp_r = cp_r logger
    member __.shellExecRun = shellExecRun logger currentDirectory
    member __.createFile = createFile logger
    member __.unzip = unzip logger
    member __.readFile = readFile logger

let writeLines (lines: string list) (stream: StreamWriter) =
    lines |> List.iter stream.WriteLine
