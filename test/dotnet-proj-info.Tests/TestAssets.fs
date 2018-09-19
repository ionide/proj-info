module DotnetProjInfo.TestAssets

open FileUtils

type TestAssetProjInfo =
  { ProjDir: string;
    AssemblyName: string;
    ProjectFile: string }

let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-oldsdk-lib";
    AssemblyName = "Lib1";
    ProjectFile = "l1"/"l1.fsproj" }

let ``samples2 NetSdk library`` =
  { ProjDir = "sample2-netsdk-lib";
    AssemblyName = "n1";
    ProjectFile = "n1"/"n1.fsproj" }
