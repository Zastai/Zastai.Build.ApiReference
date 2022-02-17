# Zastai.Build.Reference  [![Build Status][CI-S]][CI-L]

Extends an MSBuild project to produce an API reference source for each
output assembly.

This source is not intended to be compilable; rather the intent is for
it to be kept under source control, so that any changes to public API
are easy to detect and track.

Currently, the output is always (pseudo) C#. The intent is to extend
this to other formats (e.g. VB, F#, MarkDown-with-C#, ...) at a later
date.

## Configuration

The following properties can be set in the project the affect the
behaviour:

| Property                            | Description                                                                                                             |
|-------------------------------------|-------------------------------------------------------------------------------------------------------------------------|
| ApiReferenceLibraryPath             | The directories to search for the dependencies of the output assemblies (separated by semicolons).                      |
| ApiReferenceOutputDir               | The directory where the API Reference source is created. Defaults to `$(TargetDir)`.                                    |
| ApiReferenceOutputExt               | The extension used for the API Reference source. Defaults to `.cs`.                                                     |
| ApiReferenceOutputPath              | The full path of the API Reference source. Defaults to `$(ApiReferenceOutputDir)$(TargetName)$(ApiReferenceOutputExt)`. |
| GenerateApiReference                | Determines whether API Reference sources will be generated for each assembly. Defaults to `true`.                       |
| MonoRunner                          | The program used to run the generator under Mono. Defaults to `mono --runtime=v4.0.30319`.                              |
| NetCoreRunner                       | The program used to run the generator under .NET Core. Defaults to `dotnet`.                                            |
| SkipApiReferenceOutputPathFileWrite | Determines whether the output files are registered in `@(FileWrites)`. Defaults to `false`.                             |

## Release Notes

These are available [on GitHub][GHReleases].

[CI-S]: https://img.shields.io/appveyor/build/zastai/zastai-build-reference
[CI-L]: https://ci.appveyor.com/project/Zastai/zastai-build-reference

[GHReleases]: https://github.com/Zastai/Zastai.Build.Reference/releases
