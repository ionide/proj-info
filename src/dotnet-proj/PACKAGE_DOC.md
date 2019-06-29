# Features

- get properties
- get fsc/csc command line arguments
- get project to project references

Not project specific

- list installed .NET Framework versions
- get references path of .NET asseblies like `System`, `System.Data`

Support both project sdk:

- dotnet/sdk style projects (slim proj, usually .net core)
- old sdk projects (verbose proj, usually .NET)


Works on mono and windows, and allow to specify the `dotnet` or `msbuild` to use

## as .NET Tool

Install it globally with

```
dotnet tool install dotnet-proj -g
```

See help with

```
dotnet proj --help
```

to show

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

To get subcommands help, run it like `dotnet proj fsc-args --help`

Some subcommands support args of .NET Core Sdk (`dotnet`), like:

- `-c` or `--configuration`
- `-f` or `--framework`
- `-r` or `--runtime`

like

```
dotnet fsc-args -c Release -f netcoreapp2.1
```

And to specify the project

```
dotnet proj fsc-args # will search fsproj in current dir
dotnet proj fsc-args path/to/my.fsproj
```

See [examples](https://github.com/enricosada/dotnet-proj-info/tree/master/examples) directory for a quick tutorial

## Used by

- [Fable compiler](https://github.com/fable-compiler/fable) to parse fsproj projects with `dotnet fable`
- [FsAutocomplete (FSAC)](https://github.com/fsharp/FsAutoComplete/) to parse projects. That's the language server that add F# support in:
  - [Ionide in Visual Studio Code](https://github.com/ionide/ionide-vscode-fsharp)
  - [F# vim binding](https://github.com/fsharp/vim-fsharp)
  - [F# Emacs mode](https://github.com/fsharp/emacs-fsharp-mode)
