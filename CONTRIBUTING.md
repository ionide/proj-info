## Build

1. Clone repo.
2. Install local tools with `dotnet tool restore`
3. Run tests with `dotnet run --project .\build\ -- -t Test`
4. Create packages with `dotnet run --project .\build\ -- -t Pack`

## Release

1. Update version in CHANGELOG.md and add notes
    1. If possible link the pull request of the changes and mention the author of the pull request
2. Create new commit
    1. `git add CHANGELOG.md`
    1. `git commit -m "changelog for v0.45.0"`
3. Make a new version tag (for example, `v0.45.0`)
    1. `git tag v0.45.0`
4. Push changes to the repo.
    1. `git push --atomic origin main v0.45.0`


## Nighty

To use the `-nightly` packages, you'll need to add a custom nuget feed for FSharp.Compiler.Service:

- https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet7/nuget/v3/index.json
