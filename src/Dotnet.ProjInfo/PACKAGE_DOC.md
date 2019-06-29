# Features

- get properties
- get project to project references
- get fsc/csc command line arguments
- list installed .NET Framework versions
- get references path of .NET asseblies like `System`, `System.Data`

Support both project sdk:

- dotnet/sdk style projects (slim proj, usually .net core)
- old sdk projects (verbose proj, usually .NET)


Works on mono and windows, and allow to specify the `dotnet` or `msbuild` to use

## Used by

- [Fable compiler](https://github.com/fable-compiler/fable) to parse fsproj projects with `dotnet fable`
- [FsAutocomplete (FSAC)](https://github.com/fsharp/FsAutoComplete/) to parse projects. That's the language server that add F# support in:
  - [Ionide in Visual Studio Code](https://github.com/ionide/ionide-vscode-fsharp)
  - [F# vim binding](https://github.com/fsharp/vim-fsharp)
  - [F# Emacs mode](https://github.com/fsharp/emacs-fsharp-mode)
