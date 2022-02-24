# Zastai.Build.Reference  [![Build Status][CI-S]][CI-L]

Extends an MSBuild project to produce an API reference source for each
output assembly.

This source is not intended to be compilable; rather the intent is for
it to be kept under source control, so that any changes to public API
are easy to detect and track.

## Configuration

The following properties can be set in the project the affect the
behaviour:

| Property                            | Description                                                                                                                                                                                        |
|-------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| ApiReferenceFormat                  | The format to use for the API Reference source.<br/>Supported Values: `C#`, `cs` or `csharp` for C#.                                                                                               |
| ApiReferenceLibraryPath             | The directories to search for the dependencies of the output assemblies (separated by semicolons).<br/>If not specified, it will use the list of referenced assemblies as determined by the build. |
| ApiReferenceOutputDir               | The directory where the API Reference source is created.<br/>Defaults to `$(TargetDir)`.                                                                                                           |
| ApiReferenceOutputExt               | The extension used for the API Reference source.<br/>Defaults to `.cs` when the format is C#.                                                                                                      |
| ApiReferenceOutputPath              | The full path of the API Reference source.<br/>Defaults to `$(ApiReferenceOutputDir)$(TargetName)$(ApiReferenceOutputExt)`.                                                                        |
| CreateApiReferenceOutputDir         | Determines whether the directory part of `ApiReferenceOutputPath` will be created as part of the processing.<br/>Defaults to `false`.                                                              |
| GenerateApiReference                | Determines whether API Reference sources will be generated for each assembly.<br/>Defaults to `true`.                                                                                              |
| MonoRunner                          | The program used to run the generator under Mono.<br/>Defaults to `mono --runtime=v4.0.30319`.                                                                                                     |
| NetCoreRunner                       | The program used to run the generator under .NET Core.<br/>Defaults to `dotnet`.                                                                                                                   |
| SkipApiReferenceOutputPathFileWrite | Determines whether the output files are registered in `@(FileWrites)`.<br/>Defaults to `false`.                                                                                                    |

## Release Notes

These are available [on GitHub][GHReleases].

[CI-S]: https://img.shields.io/appveyor/build/zastai/zastai-build-reference
[CI-L]: https://ci.appveyor.com/project/Zastai/zastai-build-reference

[GHReleases]: https://github.com/Zastai/Zastai.Build.Reference/releases
