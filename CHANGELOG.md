# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [0.71.1] - 2025-05-17

### Fixed

- [Tweaks to solution filter loading](https://github.com/ionide/proj-info/pull/230) (thanks @Numpsy)

## [0.71.0] - 2025-05-17

### Changed

- [FSharp.Compiler.Service update 43.9.300](https://github.com/ionide/proj-info/pull/232) (thanks @TheAngryByrd)

## [0.70.2] - 2025-02-14

### Fixed

- [Fix slow binlog generation for Graph based builds](https://github.com/ionide/proj-info/pull/226) (thanks @TheAngryByrd)

## [0.70.1] - 2025-02-14

### Fixed

- [Filter out non-design time capable projects](https://github.com/ionide/proj-info/pull/225) (thanks @TheAngryByrd)

## [0.70.0] - 2025-02-12

### Changed

- [Fsharp.Compiler.Service update 43.9.201](https://github.com/ionide/proj-info/pull/223) (thanks @TheAngryByrd)

## [0.69.0] - 2025-01-02

### Changed

* We now use the [Microsoft.VisualStudio.SolutionPersistence](https://github.com/microsoft/vs-solutionpersistence) library to parse solution files. This should be more reliable and faster than the previous implementation.

## [0.68.0] - 2024-11-18

### Added

* [Support for F# 9 and .NET 9](https://github.com/ionide/proj-info/pull/219)

### Removed

* [Support for .NET 6 and 7 targets](https://github.com/ionide/proj-info/pull/219)


## [0.67.0] - 2024-10-02

* [Add CompileItem metadata to types](https://github.com/ionide/proj-info/pull/213) (thanks @Smaug123)

## [0.66.1] - 2024-09-02

### Changed

* [Add clearer message on some failure cases](https://github.com/ionide/proj-info/pull/210) (thanks @Smaug123)

## [0.66.0] - 2024-08-17

### Changed

* [Update FSharp packages for the .NET SDK 8.0.400 release](https://github.com/ionide/proj-info/pull/209)

## [0.65.0] - 2024-05-15

### Changed

* Updated F# Packages
  * FSharp.Compiler.Service to 43.8.300
  * FSharp.Core to 8.0.300

## [0.64.0] - 2024-04-21

### Added

* [Support for loading projects that are missing or have invalid Imports](https://github.com/ionide/proj-info/pull/206)
* [Traversal projects can now be loaded successfully](https://github.com/ionide/proj-info/pull/207)

### Changed

* [Removed some uses of APIs that do not work from single-file publishing](https://github.com/ionide/proj-info/pull/204)

## [0.63.0] - 2024-02-06

### Changed

* [Add support for reference assemblies to project cracking and FCS ProjectOptions mapping](https://github.com/ionide/proj-info/pull/200)

## [0.62.0] - 2023-08-21

### Changed

* Updated to FSharp.Compiler.Service version 7.0.400

## [0.61.3] - 2022-04-01

### Fixed

* [Pulling logs from MsBuild](https://github.com/ionide/proj-info/pull/189)

## [0.61.2] - 2022-02-20

### Fixed

* Updated version constraint for FSharp.Compiler.Service so that it doesn't have an upper bound

## [0.61.1] - 2022-12-09

### Fixed

- [Add missing targets - BeforeBuild and BeforeCompile](https://github.com/ionide/proj-info/pull/183) (Thanks @safesparrow!)

## [0.61.0] - 2022-11-06

### Fixed

- [Support F# reference assemblies when specified](https://github.com/ionide/proj-info/pull/177)

## [0.60.3] - 2022-10-19

### Fixed

- [Handle traversing inaccessible directories during probing](https://github.com/ionide/proj-info/pull/175) (thanks @christiansheridan)

## [0.60.2] - 2022-10-02

### Added

- [Expose existing properties on cracked projects](https://github.com/ionide/proj-info/pull/170) (thanks @TheAngryByrd!)

## [0.60.1] - 2022-09-10

### Changed

* [Fix project file loading for Graph Build](https://github.com/ionide/proj-info/pull/169) (Thanks @TheAngryByrd!)

## [0.60.0] - 2022-08-21

### Added

* [Logging for solution parsing failures](https://github.com/ionide/proj-info/pull/161) (Thanks @TheAngryByrd!)

### Fixed

* [Support running on 6.0.400 by always performing Clean builds](https://github.com/ionide/proj-info/pull/162) (Thanks @TheAngryByrd!)
* [Graph builder works for more types of projects](https://github.com/ionide/proj-info/pull/165) (Thanks @TheAngryByrd!)
* [.NET 7 previews support](https://github.com/ionide/proj-info/pull/167)

## [0.59.2] - 2022-08-04

### Changed

* [Add a method to efficiently map many ProjectOptions into FSharpProjectOptions](https://github.com/ionide/proj-info/pull/156) and [test it](https://github.com/ionide/proj-info/pull/159) (thanks @safesparrow!)

## [0.59.1] - 2022-04-16

### Changed

* [Better handling for C#/VB references](https://github.com/ionide/proj-info/pull/153)

## [0.59.0] - 2022-04-16

### Added

* [Support JSON output for the proj-info tool](https://github.com/ionide/proj-info/pull/150)
* [Support C#/VB project dll references in FCS APIs](https://github.com/ionide/proj-info/pull/148) (Thanks @nojaf!)
* [Support for projects that use references from .NET Workloads](https://github.com/ionide/proj-info/pull/152)

### Fixed

* [Potential NullRef in the project system](https://github.com/ionide/proj-info/pull/143) (Thanks @knocte!)

### Changed

* [Perf improvement on translating project data for Graph Workspaces](https://github.com/ionide/proj-info/pull/144) (Thanks @nojaf!)

## [0.58.2] - 2022-04-04

### Fixed
- Make `LegacyFrameworkDiscovery.msbuildBinary` lazy

## [0.58.1] - 2022-04-03

### Fixed

- Invalid project cache files from previous versions of this library can be detected and removed.

## [0.58.0] - 2022-04-02

### Added

- Support for loading legacy project files

### Fixed

- Saving/loading of project file caches. Perf (especially initial load) should improve massively.

## [0.57.2] - 2022-03-21

### Fixed

- Packages that depend on project-to-project references for packaging now correctly determine package versions.

## [0.57.1] - 2022-03-21

### Fixed

- Packages that depend on project-to-project references for packaging now correctly determine package versions.

## [0.57.0] - 2022-03-20

### Changed

- ProjectController.LoadProject now returns an Async bool to indicate eventual completion
- Fix heisen test <https://github.com/ionide/proj-info/issues/136>
- Multiple agents being created by ProjectSystem

## [0.56.0] - 2022-03-19

### Changed

- [Update to .NET 6](https://github.com/ionide/proj-info/pull/133)

## [0.55.4] - 2021-11-19

### Fixed

- [Fix the project references in FCS layer](https://github.com/ionide/proj-info/pull/128)
- Revert [no longer set DOTNET_HOST_PATH, instead preferring some F#-specific build parameters](https://github.com/ionide/proj-info/pull/127)

## [0.55.2] - 2021-11-17

### Changed

- [no longer set DOTNET_HOST_PATH, instead preferring some F#-specific build parameters](https://github.com/ionide/proj-info/pull/127). This should be safer for more categories of applications.

## [0.55.1] - 2021-11-16

### Fixed

- [fix the dotnet binary probing logic to not use the host app name](https://github.com/ionide/proj-info/pull/126)

## [0.55.0] - 2021-11-05

### Changed

- Updated to FCS 41

## [0.54.2] - 2021-11-01

### Fixed

- [fetch dotnet runtimes more safely](https://github.com/ionide/proj-info/pull/119) (thanks @Booksbaum!)
- [Misc. fixes for cracking found from FSAC, support normalized drive letters and resource paths](https://github.com/ionide/proj-info/pull/120/files)

## [0.54.1] - 2021-10-16

### Fixed

- Added more environment variable lookups for the `dotnet` binary, so modes like running under `dotnet test` should work more consistently.

## [0.54.0] - 2021-08-08

### Added

- The save path for binary logs can now be set

### Changed

- Reverted to FCS 39 for compatibilties' sake
- Removed dependency on MsBuild.Locator
- Massively improved compatibility for cracking projects on a broader range of SDK versions
- Fixed a regression in the MSBuild Graph Workspace Loader that resulted in it not working on SDKs greater than 5.0.10x
- centralized the setting of process-level MSBuild and dotnet SDK environment variables into the `init` function, extracting them out of the individual workspace loaders. this should ensure a consistent experience regardless of loader chosen.

## [0.53.1] - 2021-06-23

### Changed

- Fixed nuget package spec to not embed sourcelink dependency

## [0.53.0] - 2021-06-22

### Changed

- Updated to FCS 40, includes breaking changes around Project Options having extra data.

## [0.52.0] - 2021-04-13

### Changed

- [Allow global properties to be provided to project loaders](https://github.com/ionide/proj-info/pull/107)

## [0.51.0] - 2021-03-15

### Changed

- Change the order of calls so that FSAC doesn't have a deadlock with its current usage of this library

## [0.50.0] - 2021-03-13

### Changed

- Introduce a pluggable abstraction for creating workspaces to allow for independent experimentation
- Introduce a workspace implementation for MsBuild Graph Build mode, which should be a large performance boost for consumers
- introduce debouncing to prevent rebuilds when invoked multiple times in a short timeframe

## [0.49.0] - 2021-03-03

### Changed

- Added debouncing of project-cracking to prevent rework
- Changed the target of the build from "CoreCompile" to "Build" to bring in SDK props

## [0.48.0] - 2021-02-10

### Changed

- Updated to FCS 39.0.0

## [0.46.0] - 2021-01-11

### Added

- Added (again) support for generating `.binlog` files

## [0.45.2] - 2020-12-21

### Fixed

- Fixed a bug with handling `--embeded` arguments as source files

## [0.45.1] - 2020-12-21

### Fixed

- Fixed a bug with loading same project multiple times at the same time.
- Fix a bug where C# projects were passed as project references when creating `FSharpProjectOptions`

## [0.45.0] - 2020-12-19

### Changed

- Rename from `Dotnet.ProjInfo` to `Ionide.ProjInfo`
- Rewrite using MsBuild API

### Added

- Add `Ionide.ProjInfo.Sln` (ported from `enricosada/Sln`)
- Add `Ionide.ProjInfo.ProjectSystem` (ported from `fsharp/FsAutocomplete`)
- Add `Ionide.ProjInfo.FCS`

### Removed

- Remove support for old/verbose project files
- Remove CLI tool
- Remove `Ionide.ProjInfo.Workspace` - functionality now in new `Ionide.ProjInfo`
- Remove `Ionide.ProjInfo.Workspaces.FCS` - functionality now in new `Ionide.ProjInfo.FCS`

## [0.44.0] - 2020-08-11

### Added

- Last version using manual MsBuild invoke and custom targets
