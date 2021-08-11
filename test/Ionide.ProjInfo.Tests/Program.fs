// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Ionide.ProjInfo
open Ionide.ProjInfo.ProjectSystem

open Expecto
open Expecto.Impl
open Expecto.Logging


[<EntryPoint>]
let main argv =
    let baseDir = System.Environment.GetEnvironmentVariable "DOTNET_ROOT"
    // need to set this because these tests aren't run directly via the `dotnet` binary
    let dotnetExe =
        if Environment.isMacOS || Environment.isUnix then
            "dotnet"
        else
            "dotnet.exe"

    Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", IO.Path.Combine(baseDir, dotnetExe))
    let toolsPath = Init.init (IO.DirectoryInfo Environment.CurrentDirectory)

    Tests.runTests
        { defaultConfig with
              printer = TestPrinters.summaryPrinter defaultConfig.printer
              verbosity = LogLevel.Info }
        (Tests.tests toolsPath)
