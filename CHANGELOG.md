# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.45.1-preview03] - 2020-12-21

### Fixed

- Fixed a bug with loading same project multiple times at the same time.


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