## Build

1. Clone repo.
2. Install local tools with `dotnet tool restore`
3. Run tests with `dotnet run --project .\build\ -- -t Test`
4. Create packages with `dotnet run --project .\build\ -- -t Pack`

## Release

1. Update version in CHANGELOG.md
2. Create new commit
3. Make a new version tag (for example, `v0.45.0`)
4. Push changes to the repo.
