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

let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-oldsdk-lib"
    AssemblyName = "Lib1"
    ProjectFile = "l1"/"l1.fsproj"
    TargetFrameworks =  Map.ofList [
      "net461", sourceFiles ["AssemblyInfo.fs"; "Library.fs"]
    ]
    ProjectReferences = [] }

let ``samples2 NetSdk library`` =
  { ProjDir = "sample2-netsdk-lib"
    AssemblyName = "n1"
    ProjectFile = "n1"/"n1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", sourceFiles ["Library.fs"]
    ]
    ProjectReferences = [] }

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

let ``samples4 NetSdk multi tfm`` =
  { ProjDir = "sample4-netsdk-multitfm"
    AssemblyName = "m1"
    ProjectFile = "m1"/"m1.fsproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", (sourceFiles ["LibraryA.fs"] |> andProps ["MyProperty", "AAA"])
      "net461", (sourceFiles ["LibraryB.fs"] |> andProps ["MyProperty", "BBB"])
    ]
    ProjectReferences = [] }

let ``samples5 NetSdk CSharp library`` =
  { ProjDir = "sample5-netsdk-lib-cs"
    AssemblyName = "l2"
    ProjectFile = "l2"/"l2.csproj"
    TargetFrameworks =  Map.ofList [
      "netstandard2.0", sourceFiles ["Class1.cs"]
    ]
    ProjectReferences = [] }
