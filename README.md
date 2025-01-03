# Ionide.ProjInfo

[![NuGet](https://img.shields.io/nuget/v/Ionide.ProjInfo.svg)](https://www.nuget.org/packages/Ionide.ProjInfo/) ![GitHub Workflow Status](https://img.shields.io/github/workflow/status/Ionide/dotnet-proj-info/Build?style=flat-square)

Parsing and evaluating of `.fsproj` files. This repository contains several packages:
* `Ionide.ProjInfo` - library for parsing and evaluating `.fsproj` files, using `Microsoft.Build` libraries
* `Ionide.ProjInfo.FCS` - library providing utility for mapping project data types used by `Ionide.ProjInfo` into `FSharpProjectOptions` type used by `FSharp.Compiler.Service`
* `Ionide.ProjInfo.ProjectSystem` - library providing high level project system component that can be used by editor tooling. It supports features like tracking changes, event-driven notifications about project loading status, and persistent caching of the data for fast initial load.
* `Ionide.ProjInfo.Tool` - a CLI tool intended to help with debugging the cracking of various projects easily

---
You can support Ionide development on [Open Collective](https://opencollective.com/ionide).

[![Open Collective](https://opencollective.com/ionide/donate/button.png?color=blue)](https://opencollective.com/ionide)

---

## Used by:

- [Fable compiler](https://github.com/fable-compiler/fable) to parse fsproj projects with `dotnet fable`
- [FsAutocomplete (FSAC)](https://github.com/fsharp/FsAutoComplete/) to parse projects. That's the language server that add F# support in:
  - [Ionide in Visual Studio Code](https://github.com/ionide/ionide-vscode-fsharp)
  - [F# vim binding](https://github.com/fsharp/vim-fsharp)
  - [F# Emacs mode](https://github.com/fsharp/emacs-fsharp-mode)
- [F# Formatting](https://github.com/fsprojects/FSharp.Formatting)
- [FSharpLint](https://github.com/fsprojects/FSharpLint)

## Deprecated

- as .NET Core Tool: [![NuGet](https://img.shields.io/nuget/v/dotnet-proj.svg)](https://www.nuget.org/packages/dotnet-proj/)
- as dotnet cli tool: `dotnet proj-info` [![NuGet](https://img.shields.io/nuget/v/dotnet-proj-info.svg)](https://www.nuget.org/packages/dotnet-proj-info).
- old libraries (`Dotnet.ProjInfo.*`): [![NuGet](https://img.shields.io/nuget/v/Dotnet.ProjInfo.svg)](https://www.nuget.org/packages/Dotnet.ProjInfo/)

## Getting started

This project loads some MSBuild specific assemblies at runtime. Somewhat similar to how [MSBuildLocator](https://github.com/microsoft/MSBuildLocator) loads the correct assemblies.
Because of this you need to add a direct dependency on `Microsoft.Build.Framework` and `NuGet.Frameworks` but keep excluded them at runtime.

```
<PackageReference Include="Microsoft.Build.Framework" Version="17.2.0" ExcludeAssets="runtime" PrivateAssets="all" />
<PackageReference Include="NuGet.Frameworks" Version="6.2.1" ExcludeAssets="runtime" PrivateAssets="all" />
<PackageReference Include="Ionide.ProjInfo" Version="0.59.2" />
```

Next, you first need to initialize the MsBuild integration.

```fsharp
open Ionide.ProjInfo

let projectDirectory: DirectoryInfo = yourProjectOrSolutionFolder
let toolsPath = Init.init projectDirectory None
```

With the `toolsPath` you can create a `loader`

```fsharp
let defaultLoader: IWorkspaceLoader = WorkspaceLoader.Create(toolsPath, [])
// or
let graphLoader: IWorkspaceLoader = WorkspaceLoaderViaProjectGraph.Create(toolsPath, [])
```

Using the `IWorkspaceLoader` you can load projects or solutions.
Events are being emitted while projects/solutions are loaded.
You typically want to subscribe to this before you load anything.

```fsharp
let subscription: System.IDisposable = defaultLoader.Notifications.Subscribe(fun msg -> printfn "%A" msg)
let projectOptions = loader.LoadProjects([ yourFsProjPath ]) |> Seq.toArray
```

## How to contribute

*Imposter syndrome disclaimer*: I want your help. No really, I do.

There might be a little voice inside that tells you you're not ready; that you need to do one more tutorial, or learn another framework, or write a few more blog posts before you can help me with this project.

I assure you, that's not the case.

This project has some clear Contribution Guidelines and expectations that you can [read here](https://github.com/ionide/dotnet-proj-info/blob/main/CONTRIBUTING.md).

The contribution guidelines outline the process that you'll need to follow to get a patch merged. By making expectations and process explicit, I hope it will make it easier for you to contribute.

And you don't just have to write code. You can help out by writing documentation, tests, or even by giving feedback about this work. (And yes, that includes giving feedback about the contribution guidelines.)

Thank you for contributing!


## Contributing and copyright

The project is hosted on [GitHub](https://github.com/ionide/dotnet-proj-info) where you can [report issues](https://github.com/ionide/dotnet-proj-info/issues), fork
the project and submit pull requests.

The library is available under [MIT license](https://github.com/ionide/dotnet-proj-info/blob/master/LICENSE.md), which allows modification and redistribution for both commercial and non-commercial purposes.

Please note that this project is released with a [Contributor Code of Conduct](CODE_OF_CONDUCT.md). By participating in this project you agree to abide by its terms.
