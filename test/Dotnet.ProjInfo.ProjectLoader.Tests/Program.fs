// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Dotnet.ProjInfo

open Expecto


[<EntryPoint>]
let main argv =
    let toolsPath = Init.init ()
    Tests.runTests defaultConfig (Tests.tests toolsPath)