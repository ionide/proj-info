// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Ionide.ProjInfo
open Ionide.ProjInfo.ProjectSystem

open Expecto
open Expecto.Impl
open Expecto.Logging
open FsLibLog.Providers.Expecto

let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None

[<Tests>]
let tests = Tests.tests toolsPath

Ionide.ProjInfo.Logging.LogProvider.setLoggerProvider (ExpectoLogProvider())


[<EntryPoint>]
let main argv =
    let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None

    Tests.runTestsWithArgs
        {
            defaultConfig with
                printer = TestPrinters.summaryPrinter defaultConfig.printer
                verbosity = LogLevel.Verbose
        }
        argv
        tests
