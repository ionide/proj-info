namespace Dotnet.ProjInfo.Workspace

module MSBuildInfo =

  open System
  open System.IO

  let private vsSkus = ["Community"; "Professional"; "Enterprise"; "BuildTools"]
  let private vsVersions = ["2017"; "2019"]
  module private EnvUtils =
      // Below code slightly modified from FSAC and from FAKE MSBuildHelper.fs

      let private tryFindFile dirs file =
          let files =
              dirs
              |> Seq.map (fun (path : string) ->
                  try
                     let path =
                        if path.StartsWith("\"") && path.EndsWith("\"")
                        then path.Substring(1, path.Length - 2)
                        else path
                     let dir = new DirectoryInfo(path)
                     if not dir.Exists then ""
                     else
                         let fi = new FileInfo(Path.Combine(dir.FullName, file))
                         if fi.Exists then fi.FullName
                         else ""
                  with
                  | _ -> "")
              |> Seq.filter ((<>) "")
              |> Seq.toList
          files

      let Stringsplit (splitter: char) (s: string) = s.Split([| splitter |], StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

      let tryFindPath backupPaths tool =
          let paths = Environment.GetEnvironmentVariable "PATH" |> Stringsplit Path.PathSeparator
          tryFindFile (paths @ backupPaths) tool

  // TODO stop guessing, use vswhere
  // NOTE was msbuild function in FSAC
  let installedMSBuilds () =

    if not (Utils.isWindows ()) then
      ["msbuild"] // we're way past 5.0 now, time to get updated
    else
        // TODO remove shadowing
        let programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)

        let vsRoots =
          List.allPairs vsVersions vsSkus
          |> List.map (fun (version, sku) -> Path.Combine(programFilesX86, "Microsoft Visual Studio", version, sku))

        let legacyPaths =
          let programFilesPaths =
            [ @"MSBuild\Current\Bin"
              @"MSBuild\15.0\Bin"
              @"MSBuild\14.0\Bin"
              @"MSBuild\12.0\Bin"
              @"MSBuild\12.0\Bin\amd64" ]
            |> List.map (fun p -> Path.Combine(programFilesX86, p))

          programFilesPaths @
            [ @"c:\Windows\Microsoft.NET\Framework\v4.0.30319"
              @"c:\Windows\Microsoft.NET\Framework\v4.0.30128"
              @"c:\Windows\Microsoft.NET\Framework\v3.5" ]

        let sideBySidePaths =
          vsRoots
          |> List.map (fun root -> Path.Combine(root, "MSBuild", "15.0", "bin") )

        let ev = Environment.GetEnvironmentVariable "MSBuild"
        if not (String.IsNullOrEmpty ev) then [ev]
        else EnvUtils.tryFindPath (sideBySidePaths @ legacyPaths) "MsBuild.exe"
