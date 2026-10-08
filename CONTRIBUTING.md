## Build

1. Clone repo.
2. Install local tools with `dotnet tool restore`

## Testing

Testing against different .NET runtimes is a bit of a chore because we want to let the runtime load the current runtime's Microsoft.Build assemblies. Unfortunately, we can't have the `global.json` set to `net9.0` because the `Microsoft.Build` assemblies are compatible with the .NET 9 runtime, and will try to load them, even in a net8.0 TFM context.

Our current algorithm is

* run `dotnet --version` in the workspace directory
* use the output of that to determine which SDK to load from

So if you set global.json to a 9.0.xxx SDK, you'll _always_ use the 9.x MSBuild libraries, which will require the 9.0 runtime to load.  If you want to test while loading older MSBuilds, you'll need to somehow constraint the tests to 8.0.xxx SDKs, and the easiest way to do this is to make a global.json with a 8.0.xxx version and a  `rollForward: latestPatch` constraint on it.

### Running tests from the build script

Our `build.fsx` script will handle creating/deleting a temporary `global.json` file in the `test` directory for you.

1. `dotnet fsi build.fsx -- -p Test`
    1. This will build the solution and run the tests against each runtime, one stage per target framework:
        * `Test net8.0`
        * `Test net9.0`
        * `Test net10.0`

`dotnet fsi build.fsx` without a pipeline also checks the formatting first, like CI does. `dotnet fsi build.fsx -- -p Format` formats the code.

### Manually invoking dotnet test

If you want to run `dotnet test` directly, you'll need to set the `global.json` file and environment variables yourself.

#### Against LTS (net8.0)
1. Move to the test project
    1. `cd test/Ionide.ProjInfo.Tests`
2. Run tests with `dotnet test`


#### Against STS (net9.0)
1. Change global.json to use net9.0
    ```json
        "sdk": {
            "version": "9.0.100",
            "rollForward": "latestMinor"
        }
    ```
1. Move to the test project
    1. `cd test/Ionide.ProjInfo.Tests`
3. Set environment variable `BuildNet9` to `true`
    1. Bash: `export BuildNet9=true`
    2. PowerShell: `$env:BuildNet9 = "true"`
4. Run tests with `dotnet test`


#### Against LTS (net10.0)
1. Change global.json to use net10.0
    ```json
        "sdk": {
            "version": "10.0.100",
            "rollForward": "latestMinor"
        }
    ```
1. Move to the test project
    1. `cd test/Ionide.ProjInfo.Tests`
3. Set environment variable `BuildNet10` to `true`
    1. Bash: `export BuildNet10=true`
    2. PowerShell: `$env:BuildNet10 = "true"`
4. Run tests with `dotnet test`


## Release

The newest version in CHANGELOG.md drives the release. Do not create tags by hand.

1. Add a new version section to CHANGELOG.md (for example, `## [0.45.0] - 2026-10-08`) with the notes
    1. If possible link the pull request of the changes and mention the author of the pull request
2. Merge the change into `main`

When `main` gets a CHANGELOG.md whose newest version has no GitHub release yet (for example, `v0.45.0`), the [Release workflow](.github/workflows/release.yml) starts a release job. That job pushes the packages to NuGet (with trusted publishing) and creates the GitHub release and its tag, with the changelog section as notes and the packages attached. If a release fails part way, rerun it with "Run workflow" on `main`.

To see what a release would do without publishing, run `dotnet fsi build.fsx -- -p Release --dry-run`.


## Nighty

To use the `-nightly` packages, you'll need to add a custom nuget feed for FSharp.Compiler.Service:

- https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet7/nuget/v3/index.json
