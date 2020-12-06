// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp

open System
open Dotnet.ProjInfo

[<EntryPoint>]
let main argv =
    let toolsPath = Init.init ()
    printfn "TOOLS: %A" toolsPath

    //let path = System.IO.Path.GetFullPath @"D:\Programowanie\Projekty\Ionide\FsAutoComplete\src\FsAutoComplete\FsAutoComplete.fsproj"
    let path = System.IO.Path.GetFullPath  "../../src/Dotnet.ProjInfo.Workspace.FCS/Dotnet.ProjInfo.Workspace.FCS.fsproj"
    // let path = System.IO.Path.GetFullPath  "Dotnet.ProjInfo.ProjectLoader.Tests.fsproj"

    let loader = WorkspaceLoader.Create(toolsPath, fun p _ -> p)
    let result = loader.LoadProject path
    printfn "%A" result
    0 // return an integer exit code