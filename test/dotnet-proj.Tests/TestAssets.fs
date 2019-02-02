module DotnetProjInfo.TestAssets

open FileUtils

type TestAssetProjInfo =
  { ProjDir: string
    AssemblyName: string
    ProjectFile: string
    TargetFrameworks: Map<string, TestAssetProjInfoByTfm>
    ProjectReferences: TestAssetProjInfo list }
and TestAssetProjInfoByTfm =
  { SourceFiles: string list
    Props: Map<string,string> }

let sourceFiles sources =
  { SourceFiles = sources
    Props = Map.empty }

let andProps props x =
  let n =
    [ yield! x.Props |> Map.toList
      yield! props ]
  { x with Props = n |> Map.ofList }

/// old sdk, one net461 lib l1
let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-oldsdk-lib"
    AssemblyName = "Lib1"
    ProjectFile = "l1"/"l1.fsproj"
    TargetFrameworks =  Map.ofList [
      "net461", sourceFiles ["AssemblyInfo.fs"; "Library.fs"]
    ]
    ProjectReferences = [] }

/// dotnet sdk, one netstandard2.0 lib n1
let ``samples2 NetSdk library`` =
  { ProjDir = "sample2-netsdk-lib"
    AssemblyName = "n1"
    ProjectFile = "n1"/"n1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", sourceFiles ["Library.fs"]
    ]
    ProjectReferences = [] }

/// dotnet sdk, a netcoreapp2.1 console c1
/// reference:
/// - netstandard2.0 lib l1 (C#)
/// - netstandard2.0 lib l2 (F#)
let ``sample3 Netsdk projs`` =
  { ProjDir = "sample3-netsdk-projs"
    AssemblyName = "c1"
    ProjectFile = "c1"/"c1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netcoreapp2.1", sourceFiles ["Program.fs"]
    ]
    ProjectReferences =
      [ { ProjDir = "sample3-netsdk-projs"/"l1"
          AssemblyName = "l1"
          ProjectFile = "l1"/"l1.csproj"
          TargetFrameworks =  Map.ofList [
            "netstandard2.0", sourceFiles ["Class1.cs"]
          ]
          ProjectReferences = [] }
        { ProjDir = "sample3-netsdk-projs"/"l2"
          AssemblyName = "l2"
          ProjectFile = "l2"/"l2.fsproj"
          TargetFrameworks =  Map.ofList [
            "netstandard2.0", sourceFiles ["Library.fs"]
          ]
          ProjectReferences = [] }
      ] }

/// dotnet sdk, m1 library multi tfm:
/// - netstandard2.0 with file LibraryA.fs and prop MyProperty=AAA
/// - net461 with file LibraryB.fs and prop MyProperty=BBB
let ``samples4 NetSdk multi tfm`` =
  { ProjDir = "sample4-netsdk-multitfm"
    AssemblyName = "m1"
    ProjectFile = "m1"/"m1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", (sourceFiles ["LibraryA.fs"] |> andProps ["MyProperty", "AAA"])
      "net461", (sourceFiles ["LibraryB.fs"] |> andProps ["MyProperty", "BBB"])
    ]
    ProjectReferences = [] }

/// dotnet sdk, a C# netstandard2.0 library l2
let ``samples5 NetSdk CSharp library`` =
  { ProjDir = "sample5-netsdk-lib-cs"
    AssemblyName = "l2"
    ProjectFile = "l2"/"l2.csproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", sourceFiles ["Class1.cs"]
    ]
    ProjectReferences = [] }

/// dotnet sdk, a c1 console app (netcoreapp) who reference:
/// - netstandard2.0 l1 library
let ``sample6 Netsdk Sparse/1`` =
  { ProjDir = "sample6-netsdk-sparse"
    AssemblyName = "c1"
    ProjectFile = "c1"/"c1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netcoreapp2.1", sourceFiles ["Program.fs"]
    ]
    ProjectReferences =
      [ { ProjDir = "sample6-netsdk-sparse"/"l1"
          AssemblyName = "l1"
          ProjectFile = "l1"/"l1.fsproj"
          TargetFrameworks =  Map.ofList [
            "netstandard2.0", sourceFiles ["Library.fs"]
          ]
          ProjectReferences = [] }
      ] }

/// dotnet sdk, a netstandard2.0 library l2
let ``sample6 Netsdk Sparse/2`` =
    { ProjDir = "sample6-netsdk-sparse"/"l2"
      AssemblyName = "l2"
      ProjectFile = "l2"/"l2.fsproj"
      TargetFrameworks =  Map.ofList [
        "netstandard2.0", sourceFiles ["Library.fs"]
      ]
      ProjectReferences = [] }

/// sln
let ``sample6 Netsdk Sparse/sln`` =
    { ProjDir = "sample6-netsdk-sparse"
      AssemblyName = ""
      ProjectFile = "sample6-netsdk-sparse.sln"
      TargetFrameworks = Map.empty
      ProjectReferences = [] }
