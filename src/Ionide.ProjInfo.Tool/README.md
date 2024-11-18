# Ionide.ProjInfo.Tool

A .NET SDK tool that allows for quick parsing of projects and solutions.

Broadly, the tool has three kinds of arguments:


### Loading args

```
--project <PATH> the path to a project file to load
--solution <PATH> the path to a solution file to load
```

### How to load a project

By default you will use the standard MSBuild loader, but specifying `--graph` will use the MSBuild graph loader.

### What to parse the results into

By default you will get a structured text version of the Ionide project options for the project(s). If you want, you can get the FCS ProjectOptions versions by adding the `--fcs` flag. Finally, you can get project JSON with `--serialize` as well.
