// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Ionide.ProjInfo
open Ionide.ProjInfo.ProjectSystem

open Expecto
open Expecto.Impl
open Expecto.Logging

let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None

[<Tests>]
let tests = Tests.tests toolsPath


[<EntryPoint>]
let main argv =
    let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None

    let args = [
        CLIArguments.Printer (TestPrinters.summaryPrinter defaultConfig.printer)
        CLIArguments.Verbosity LogLevel.Verbose
    ]

    Tests.runTestsWithCLIArgs
        args
        argv
        tests
