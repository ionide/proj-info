namespace Ionide.ProjInfo

open System

module Types =

    type ProjectSdkInfo = {
        IsTestProject: bool
        Configuration: string // Debug
        IsPackable: bool // true
        TargetFramework: string // netcoreapp1.0
        TargetFrameworkIdentifier: string // .NETCoreApp
        TargetFrameworkVersion: string // v1.0

        MSBuildAllProjects: string list //;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\FSharp.NET.Sdk\Sdk\Sdk.props;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\Sdk\Sdk.props;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.props;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.DefaultItems.props;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.SupportedTargetFrameworks.props;e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\obj\c1.fsproj.nuget.g.props;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\FSharp.NET.Sdk\Sdk\Sdk.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\Sdk\Sdk.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.BeforeCommon.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.DefaultAssemblyInfo.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.DefaultOutputPaths.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.TargetFrameworkInference.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.RuntimeIdentifierInference.targets;C:\Users\e.sada\.nuget\packages\fsharp.net.sdk\1.0.5\build\FSharp.NET.Core.Sdk.targets;e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\c1.fsproj;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Microsoft.Common.CurrentVersion.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\NuGet.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\15.0\Microsoft.Common.targets\ImportAfter\Microsoft.TestPlatform.ImportAfter.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Microsoft.TestPlatform.targets;e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\obj\c1.fsproj.nuget.g.targets;e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\obj\c1.fsproj.proj-info.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.Common.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.PackageDependencyResolution.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Sdk.DefaultItems.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.DisableStandardFrameworkResolution.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.GenerateAssemblyInfo.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.Publish.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\Microsoft.NET.Sdk\build\Microsoft.NET.PreserveCompilationContext.targets;C:\dotnetcli\dotnet-dev-win-x64.1.0.4\sdk\1.0.4\Sdks\NuGet.Build.Tasks.Pack\build\NuGet.Build.Tasks.Pack.targets
        MSBuildToolsVersion: string // 15.0

        ProjectAssetsFile: string // e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\obj\project.assets.json
        RestoreSuccess: bool // True

        Configurations: string list // Debug;Release
        TargetFrameworks: string list // netcoreapp1.0;netstandard1.6

        //may not exists
        RunArguments: string option // exec "e:\github\DotnetNewFsprojTestingSamples\sdk1.0\sample1\c1\bin\Debug\netcoreapp1.0\c1.dll"
        RunCommand: string option // dotnet

        //from 2.0
        IsPublishable: bool option
    } // true

    type ProjectReference = {
        RelativePath: string
        ProjectFileName: string
        TargetFramework: string
    }

    type Property = { Name: string; Value: string }

    type PackageReference = {
        Name: string
        Version: string
        FullPath: string
    }

    type ProjectOutputType =
        | Library
        | Exe
        | Custom of string

    type ProjectItem = Compile of name: string * fullpath: string * metadata: Map<string, string> option

    type ProjectOptions = {
        ProjectId: string option
        ProjectFileName: string
        TargetFramework: string
        SourceFiles: string list
        OtherOptions: string list
        ReferencedProjects: ProjectReference list
        PackageReferences: PackageReference list
        LoadTime: DateTime
        /// The path to the primary executable or loadable output of this project
        TargetPath: string
        /// If present, this project produced a reference assembly and this should be used as primary reference for downstream proejcts
        TargetRefPath: string option
        ProjectOutputType: ProjectOutputType
        ProjectSdkInfo: ProjectSdkInfo
        Items: ProjectItem list
        Properties: Property list
        CustomProperties: Property list
    } with
        /// ResolvedTargetPath is the path to the primary reference assembly for this project.
        /// For projects that produce ReferenceAssemblies, this is the path to the reference assembly.
        /// For other projects, this is the same as TargetPath.
        member x.ResolvedTargetPath =
            defaultArg x.TargetRefPath x.TargetPath

    /// Represents a `<Compile>` node within an fsproj file.
    type CompileItem = {
        /// The `Compile` node's Include contents, after MSBuild has finished evaluation.
        /// For example:
        ///   * `<Compile Include="Foo.fs"/>` would have this set to "Foo.fs";
        ///   * `<Compile Include="Bar/Baz.fs"/>` would have `"Bar/Baz.fs"`;
        ///   * (contrived): `<Compile Include="$(IsPackable).fs"/>` might have `true.fs` or `false.fs`, for example.
        Name: string
        /// Full path on disk to the F# file this `Compile` node is telling MsBuild to compile.
        FullPath: string
        /// Value of the `<Link />` sub-node, if one exists.
        /// For example, `<Compile Include="Foo.fs"><Link>../bar.fs</Link></Compile` would set this to
        /// "../bar.fs" (no further path resolution takes place).
        Link: string option
        /// All the other metadata in the project file associated with this Compile node.
        /// This is a map of "name of metadata key" to "contents of that key".
        /// Recall that according to MsBuild, those contents are not actually unrestricted XML, although they sure do look like it!
        /// If you try and put XML as a metadata value, MsBuild will just give it to you as a string, or will fail to load the
        /// project at all.
        ///
        /// For example:
        ///   * `<Compile Include="Foo.fs"><Something>hello</Something></Compile>` sets the metadata key "Something" to the value "hello".
        ///   * `<Compile Include="Foo.fs"><Blah Baz="hi" /></Compile>` fails at project load time.
        ///   * `<Compile Include="Foo.fs"><Blah></Blah></Compile>` sets the metadata key "Blah" to the empty string.
        ///   * `<Compile Include="Foo.fs"><Blah><Quux></Quux></Blah></Compile>` sets the metadata key "Blah" to the string "<Quux></Quux>".
        ///
        /// This map includes the `Link` value, if it exists, which was also extracted for convenience into the `Link` field
        /// of this CompileItem.
        ///
        /// If you specify the same metadata key multiple times, the last value wins. (This is MsBuild's decision, not ours.)
        Metadata: Map<string, string>
    }

    type ToolsPath = ToolsPath of string


    type GetProjectOptionsErrors =
        // projFile is duplicated in WorkspaceProjectState???
        | ProjectNotRestored of projFile: string
        | ProjectNotFound of projFile: string
        | LanguageNotSupported of projFile: string
        | ProjectNotLoaded of projFile: string
        | MissingExtraProjectInfos of projFile: string
        | InvalidExtraProjectInfos of projFile: string * error: string
        | ReferencesNotLoaded of projFile: string * referenceErrors: seq<string * GetProjectOptionsErrors>
        | GenericError of projFile: string * string

        member x.ProjFile =
            match x with
            | ProjectNotRestored projFile
            | LanguageNotSupported projFile
            | ProjectNotLoaded projFile
            | MissingExtraProjectInfos projFile
            | InvalidExtraProjectInfos(projFile, _)
            | ReferencesNotLoaded(projFile, _)
            | GenericError(projFile, _) -> projFile
            | ProjectNotFound(projFile) -> projFile

    [<RequireQualifiedAccess>]
    type WorkspaceProjectState =
        | Loading of projectFilePath: string
        | Loaded of loadedProject: ProjectOptions * knownProjects: ProjectOptions list * fromCache: bool
        | Failed of projectFilePath: string * errors: GetProjectOptionsErrors

        member x.ProjFile =
            match x with
            | Loading proj
            | Failed(proj, _) -> proj
            | Loaded(lp, _, _) -> lp.ProjectFileName

        member x.DebugPrint =
            match x with
            | Loading proj ->
                "Loading: "
                + proj
            | Loaded(lp, _, _) ->
                "Loaded: "
                + lp.ProjectFileName
            | Failed(proj, _) ->
                "Failed: "
                + proj
