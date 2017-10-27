# dotnet-proj-info

[![Build status](https://ci.appveyor.com/api/projects/status/i7piggo87r7k31t2/branch/master?svg=true)](https://ci.appveyor.com/project/enricosada/dotnet-proj-info/branch/master)
[![Build Status](https://travis-ci.org/enricosada/dotnet-proj-info.svg?branch=master)](https://travis-ci.org/enricosada/dotnet-proj-info)

- dotnet cli tool: `dotnet proj-info` [![NuGet](https://img.shields.io/nuget/v/dotnet-proj-info.svg)](https://www.nuget.org/packages/dotnet-proj-info)
- as library: `Dotnet.ProjInfo` [![NuGet](https://img.shields.io/nuget/v/Dotnet.ProjInfo.svg)](https://www.nuget.org/packages/Dotnet.ProjInfo)

Features:

- get properties
- get project to project references
- get fsc command line arguments
- list installed .NET Framework versions
- get references path of .NET asseblies like `System`, `System.Data`

Support:

- dotnet/sdk style projects (slim proj, usually .net core)
- old sdk projects (verbose proj, usually .NET)

Runtimes:

Works on mono and windows, and allow to specify the `dotnet` or `msbuild` to use

Notes:

Add usual args of .NET cli, like `-c` (`--configuration`), `-f` (`--framework`), `-r` (`--runtime`)

See [examples](https://github.com/enricosada/dotnet-proj-info/tree/master/examples) directory

```
USAGE: proj-info [--help] [--fsc-args] [--project-refs] [--get-property [<string>...]]
                 [--net-fw-references-path [<string>...]] [--installed-net-frameworks] [--framework <string>]
                 [--runtime <string>] [--configuration <string>] [--verbose] [--msbuild <string>]
                 [--dotnetcli <string>] [--msbuild-host <auto|msbuild|dotnetmsbuild>] [<string>]

PROJECT:

    <string>              the MSBuild project file

OPTIONS:

    --fsc-args            get fsc arguments
    --project-refs        get project references
    --get-property, -gp [<string>...]
                          msbuild property to get (allow multiple)
    --net-fw-references-path [<string>...]
                          list the .NET Framework references
    --installed-net-frameworks
                          list of the installed .NET Frameworks
    --framework, -f <string>
                          target framework, the TargetFramework msbuild property
    --runtime, -r <string>
                          target runtime, the RuntimeIdentifier msbuild property
    --configuration, -c <string>
                          configuration to use (like Debug), the Configuration msbuild property
    --verbose, -v         verbose log
    --msbuild <string>    MSBuild path (default "msbuild")
    --dotnetcli <string>  Dotnet CLI path (default "dotnet")
    --msbuild-host <auto|msbuild|dotnetmsbuild>
                          the Msbuild host, if auto then oldsdk=MSBuild dotnetSdk=DotnetCLI
    --help                display this list of options.

```

## Build

Run:

```bash
dotnet build
```

To run tests:

```bash
dotnet test -v n
```

To create packages:

```bash
dotnet pack
```

will create packages in `artifacts\nupkgs`

pass `/p:Version=1.2.3` to create a package with version `1.2.3`

