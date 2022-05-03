# Zastai.Build.ApiReference  [![Build Status][CI-S]][CI-L] [![NuGet Package Version][NuGet-S]][NuGet-L]

Extends an MSBuild project to produce an API reference source for each
output assembly.

This source is not intended to be compilable; rather the intent is for
it to be kept under source control, so that any changes to public API
are easy to detect and track.

## Configuration

The following properties can be set in the project to affect the
behaviour:

| Property                            | Description                                                                                                                                                                                        |
|-------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| ApiReferenceFormat                  | The format to use for the API Reference source.<br/>Supported Values: `C#` (or `cs` or `csharp`) for C#, and `C#-MarkDown` (or variations using `cs`, `csharp` and/or `md`) for MarkDown-with-C#.  |
| ApiReferenceLibraryPath             | The directories to search for the dependencies of the output assemblies (separated by semicolons).<br/>If not specified, it will use the list of referenced assemblies as determined by the build. |
| ApiReferenceOutputDir               | The directory where the API Reference source is created.<br/>Defaults to `$(TargetDir)`.                                                                                                           |
| ApiReferenceOutputExt               | The extension used for the API Reference source.<br/>Defaults to `.cs` when the format is C#, and `.cs.md` when the format is MarkDown-with-C#.                                                    |
| ApiReferenceOutputPath              | The full path of the API Reference source.<br/>Defaults to `$(ApiReferenceOutputDir)$(TargetName)$(ApiReferenceOutputExt)`.                                                                        |
| CreateApiReferenceOutputDir         | Determines whether the directory part of `ApiReferenceOutputPath` will be created as part of the processing.<br/>Defaults to `false`.                                                              |
| GenerateApiReference                | Determines whether API Reference sources will be generated for each assembly.<br/>Defaults to `true`.                                                                                              |
| MonoRunner                          | The program used to run the generator under Mono.<br/>Defaults to `mono --runtime=v4.0.30319`.                                                                                                     |
| NetCoreRunner                       | The program used to run the generator under .NET Core.<br/>Defaults to `dotnet`.                                                                                                                   |
| SkipApiReferenceOutputPathFileWrite | Determines whether the output files are registered in `@(FileWrites)`.<br/>Defaults to `false`.                                                                                                    |

In addition, the choice of which attributes to consider part of the
public API is based on two item groups:

| Property                     | Description                                                                                                                 |
|------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| ApiReferenceIncludeAttribute | Attributes to include. If not specified, all attributes are include, unless excluded via `@(ApiReferenceExcludeAttribute)`. |
| ApiReferenceExcludeAttribute | Attributes to exclude. Applies to attributes included via `@(ApiReferenceIncludeAttribute)`.                                |

In both cases, shell wildcards (`?` and `*`) are supported. Names match
against the full internal name of the attribute type (like
``Namespace.GenericTypeName`2/NestedAttribute``).
Attributes handled as part of syntax generation (like
`System.ParamArrayAttribute` and
`System.Runtime.CompilerServices.ExtensionAttribute`) are never
included.

## Release Notes

These are available [on GitHub][GHReleases].

## Credits

Package icon created by [DinosoftLabs - FlatIcon][PackageIcon].

[CI-S]: https://img.shields.io/appveyor/build/zastai/zastai-build-apireference
[CI-L]: https://ci.appveyor.com/project/Zastai/zastai-build-apireference

[NuGet-S]: https://img.shields.io/nuget/v/Zastai.Build.ApiReference
[NuGet-L]: https://www.nuget.org/packages/Zastai.Build.ApiReference

[GHReleases]: https://github.com/Zastai/Zastai.Build.APIReference/releases
[PackageIcon]: https://www.flaticon.com/free-icon/browser_718064
