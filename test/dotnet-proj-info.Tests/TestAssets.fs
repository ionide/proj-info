module DotnetProjInfo.TestAssets

open FileUtils

type TestAssetProjInfo =
  { ProjDir: string;
    PackageName: string;
    AssemblyName: string;
    Files: string list }

let ``samples1 OldSdk library`` =
  { ProjDir = "sample1-dotnet";
    PackageName = "Lib1";
    AssemblyName = "Lib1";
    Files = [ "lib"/"net45"/"Lib1.dll" ] }
