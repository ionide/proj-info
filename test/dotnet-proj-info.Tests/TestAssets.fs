module DotnetProjInfo.TestAssets

open FileUtils

type TestAssetProjInfo =
  { ProjDir: string;
    PackageName: string;
    AssemblyName: string;
    ProjectFile: string }

let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-oldsdk-lib";
    PackageName = "Lib1";
    AssemblyName = "Lib1";
    ProjectFile = "l1"/"l1.fsproj" }
