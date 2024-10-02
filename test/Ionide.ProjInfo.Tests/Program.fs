open System
open Ionide.ProjInfo
open Expecto
open Expecto.Impl
open Expecto.Logging
open System.Threading

let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None

[<Tests>]
let tests = Tests.tests toolsPath

[<EntryPoint>]
let main argv =
    let _ = Init.init (IO.DirectoryInfo Environment.CurrentDirectory) None
    use cts = new CancellationTokenSource()
    Console.CancelKeyPress.Add(fun _ -> cts.Cancel())
    AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> cts.Cancel())

    runTestsWithCLIArgsAndCancel cts.Token [
            Printer (TestPrinters.summaryPrinter defaultConfig.printer)
            Verbosity Verbose
        ] argv tests
