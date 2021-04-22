# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.53.0-beta01] - 2021-04-21

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