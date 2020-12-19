### Ported from: https://github.com/enricosada/sln

WHY? no official sln parser avaiable

https://github.com/Microsoft/msbuild/issues/1708#issuecomment-280693611

Reuse some source files from https://github.com/Microsoft/msbuild/ repo
and reimplement some classes to not import too many files

Original file list:

```xml
<Compile Include="..\..\vendor\msbuild\src\Shared\ErrorUtilities.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\VisualStudioConstants.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\Constants.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\Traits.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\BuildEventFileInfo.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\EscapingUtilities.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\StringBuilderCache.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\OpportunisticIntern.cs" />
<Compile Include="..\..\vendor\msbuild\src\Shared\ProjectFileErrorUtilities.cs" />
<Compile Include="..\..\vendor\msbuild\src\Build\Errors\InvalidProjectFileException.cs" />
<Compile Include="..\..\vendor\msbuild\src\Build\Construction\Solution\ProjectConfigurationInSolution.cs" />
<Compile Include="..\..\vendor\msbuild\src\Build\Construction\Solution\ProjectInSolution.cs" />
<Compile Include="..\..\vendor\msbuild\src\Build\Construction\Solution\SolutionConfigurationInSolution.cs" />
<Compile Include="..\..\vendor\msbuild\src\Build\Construction\Solution\SolutionFile.cs" />
```