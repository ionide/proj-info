namespace Dotnet.ProjInfo.Workspace

module MSBuildInfo =

  open System
  open System.IO

  let private programFilesX86 () =
      let environVar v = Environment.GetEnvironmentVariable v

      let wow64 = environVar "PROCESSOR_ARCHITEW6432"
      let globalArch = environVar "PROCESSOR_ARCHITECTURE"
      match wow64, globalArch with
      | "AMD64", "AMD64"
      | null, "AMD64"
      | "x86", "AMD64" -> environVar "ProgramFiles(x86)"
      | _ -> environVar "ProgramFiles"
      |> fun detected -> if detected = null then @"C:\Program Files (x86)\" else detected

  let private vsSkus = ["Community"; "Professional"; "Enterprise"; "BuildTools"]
  let private vsVersions = ["2017"]
  let cartesian a b =
    [ for a' in a do
        for b' in b do
          yield a', b' ]

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

      // TODO remove shadowing
      let programFilesX86 = programFilesX86 ()

      let vsRoots =
        cartesian vsVersions vsSkus 
        |> List.map (fun (version, sku) -> Path.Combine(programFilesX86, "Microsoft Visual Studio", version, sku))

      if not (Utils.isWindows ()) then
        ["msbuild"] // we're way past 5.0 now, time to get updated
      else
        let legacyPaths =
            [ Path.Combine(programFilesX86, @"MSBuild\14.0\Bin")
              Path.Combine(programFilesX86, @"MSBuild\12.0\Bin")
              Path.Combine(programFilesX86, @"MSBuild\12.0\Bin\amd64")
              @"c:\Windows\Microsoft.NET\Framework\v4.0.30319"
              @"c:\Windows\Microsoft.NET\Framework\v4.0.30128"
              @"c:\Windows\Microsoft.NET\Framework\v3.5" ]

        let sideBySidePaths =
          vsRoots
          |> List.map (fun root -> Path.Combine(root, "MSBuild", "15.0", "bin") )

        let ev = Environment.GetEnvironmentVariable "MSBuild"
        if not (String.IsNullOrEmpty ev) then [ev]
        else EnvUtils.tryFindPath (sideBySidePaths @ legacyPaths) "MsBuild.exe"
