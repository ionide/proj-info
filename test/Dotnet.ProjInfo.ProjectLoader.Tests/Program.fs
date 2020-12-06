// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Dotnet.ProjInfo

[<EntryPoint>]
let main argv =
    let toolsPath = ProjectLoader.init ()
    printfn "TOOLS: %s" toolsPath
    //let path = System.IO.Path.GetFullPath @"D:\Programowanie\Projekty\Ionide\FsAutoComplete\src\FsAutoComplete\FsAutoComplete.fsproj"
    let path = System.IO.Path.GetFullPath  "../../src/Dotnet.ProjInfo.Workspace.FCS/Dotnet.ProjInfo.Workspace.FCS.fsproj"
    // let path = System.IO.Path.GetFullPath  "Dotnet.ProjInfo.ProjectLoader.Tests.fsproj"

    printfn "PATH: %s" path
    let result = ProjectLoader.getProjectInfo path toolsPath

    printfn "%A" result
    0 // return an integer exit code