module DotnetProjInfo.TestAssets

open FileUtils

type TestAssetProjInfo =
  { ProjDir: string;
    AssemblyName: string;
    ProjectFile: string;
    ProjectReferences: TestAssetProjInfo list }

let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-oldsdk-lib";
    AssemblyName = "Lib1";
    ProjectFile = "l1"/"l1.fsproj"
    ProjectReferences = [] }

let ``samples2 NetSdk library`` =
  { ProjDir = "sample2-netsdk-lib";
    AssemblyName = "n1";
    ProjectFile = "n1"/"n1.fsproj";
    ProjectReferences = [] }

let ``sample3 Netsdk projs`` =
  { ProjDir = "sample3-netsdk-projs";
    AssemblyName = "c1";
    ProjectFile = "c1"/"c1.fsproj";
    ProjectReferences =
      [ { ProjDir = "sample3-netsdk-projs"/"l1";
          AssemblyName = "l1";
          ProjectFile = "l1"/"l1.csproj";
          ProjectReferences = [] }
        { ProjDir = "sample3-netsdk-projs"/"l2";
          AssemblyName = "l2";
          ProjectFile = "l2"/"l2.fsproj";
          ProjectReferences = [] }
      ] }

let ``samples4 NetSdk multi tfm`` =
  { ProjDir = "sample4-netsdk-multitfm";
    AssemblyName = "m1";
    ProjectFile = "m1"/"m1.fsproj";
    ProjectReferences = [] }
