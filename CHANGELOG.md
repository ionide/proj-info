# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
