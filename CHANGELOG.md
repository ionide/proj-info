# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.47.0] - 2021-02-10

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