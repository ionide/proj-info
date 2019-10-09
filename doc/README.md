# Info

## How to release a new version

For example you want to release version `1.2.3`

1. check that the version `1.2.3` is the one in `Directory.Build.props`, in the `Version` property
2. git tag `v1.2.3` and push it (note the `v` prefix)
3. Appveyor will trigger a job based on that tag, and add the generated nupkgs in a new [github release](https://github.com/enricosada/dotnet-proj-info/releases)
4. Go in the appveyor job, and click `Deploy` to push to nuget.org
