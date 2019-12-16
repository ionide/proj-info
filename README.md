
[![Build status](https://ci.appveyor.com/api/projects/status/i7piggo87r7k31t2/branch/master?svg=true)](https://ci.appveyor.com/project/enricosada/dotnet-proj-info/branch/master)

# dotnet-proj-info

- as .NET Core Tool: [![NuGet](https://img.shields.io/nuget/v/dotnet-proj.svg)](https://www.nuget.org/packages/dotnet-proj/)
- as library: `Dotnet.ProjInfo` [![NuGet](https://img.shields.io/nuget/v/Dotnet.ProjInfo.svg)](https://www.nuget.org/packages/Dotnet.ProjInfo)

## Features

- get properties
- get `fsc`/`csc` command line arguments
- get project to project references
- list installed .NET Framework versions
- get references path of .NET asseblies like `System`, `System.Data`

Support both project sdk:

- dotnet/sdk style projects (slim proj, usually .net core)
- old sdk projects (verbose proj, usually .NET)

Works on mono and windows .NET, and allow to specify the `dotnet` (.NET Core) or `msbuild` (.NET) to use

## as .NET Tool

Install with:

```bash
dotnet tool install -g dotnet-proj
```

and

```bash
dotnet proj --help
```

Usage:

```
dotnet-proj.
 
USAGE: dotnet-proj [--help] [--verbose] [<subcommand> [<options>]]

SUBCOMMANDS:

    prop <options>        get properties
    fsc-args <options>    get fsc arguments
    csc-args <options>    get csc arguments
    p2p <options>         get project references
    net-fw <options>      list the installed .NET Frameworks
    net-fw-ref <options>  get the reference path of given .NET Framework assembly

    Use 'dotnet-proj <subcommand> --help' for additional information.

OPTIONS:

    --verbose, -v         verbose log
    --help                display this list of options.
```

Subcommands support usual arguments of .NET cli (`dotnet`) where make sense, like:

- the project to use
- `-c` or `--configuration`
- `-f` or `--framework`
- `-r` or `--runtime`

See [examples](https://github.com/enricosada/dotnet-proj-info/tree/master/examples) directory for a quick tutorial

## as Library

Used by:

- [Fable compiler](https://github.com/fable-compiler/fable) to parse fsproj projects with `dotnet fable`
- [FsAutocomplete (FSAC)](https://github.com/fsharp/FsAutoComplete/) to parse projects. That's the language server that add F# support in:
  - [Ionide in Visual Studio Code](https://github.com/ionide/ionide-vscode-fsharp)
  - [F# vim binding](https://github.com/fsharp/vim-fsharp)
  - [F# Emacs mode](https://github.com/fsharp/emacs-fsharp-mode)

## Build

Clone repo.

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

will create packages in `bin\nupkgs`

pass `/p:Version=1.2.3` to create a package with version `1.2.3`

## Deprecated

- as dotnet cli tool: `dotnet proj-info` [![NuGet](https://img.shields.io/nuget/v/dotnet-proj-info.svg)](https://www.nuget.org/packages/dotnet-proj-info). Use `dotnet proj` as .NET Core Tool instead, can be installed locally with `dotnet tool install --tool-path` instead of `-g`
