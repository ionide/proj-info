# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Rewrite using MsBuild API
- Add `Dotnet.ProjInfo.Sln` (ported from `enricosada/Sln`)
- Add `Dotnet.ProjInfo.ProjectSystem` (ported from `fsharp/FsAutocomplete`)

### Removed
- Remove support for old/verbose project files
- Remove CLI tool
- Remove `Dotnet.ProjInfo.Workspace` - functionality now in new `Dotnet.ProjInfo`
- Remove `Dotnet.ProjInfo.Workspaces.FCS` - functionality now in new `Dotnet.ProjInfo.FCS`

## [0.44.0] - 2020-08-11

### Added
- Last version using manual MsBuild invoke and custom targets