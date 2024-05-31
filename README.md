# Zastai.Build.ApiReference  [![Build Status][CI-S]][CI-L] [![NuGet Package Version][NuGet-S]][NuGet-L]

Extends an MSBuild project to produce an API reference source for each
output assembly.

This source is not intended to be compilable; rather the intent is for
it to be kept under source control, so that any changes to public API
are easy to detect and track.

## Configuration - Properties

The following properties can be set in the project to affect the
behaviour:

### ApiReferenceFormat

The format to use for the API Reference source.

Supported Values: `C#` (or `cs` or `csharp`) for C#, and `C#-MarkDown`
(or variations using `cs`, `csharp` instead of `C#` and/or `md` instead
of `MarkDown`) for MarkDown-with-C#.

### ApiReferenceLibraryPath

The directories to search for the dependencies of the output assemblies
(separated by semicolons).

If not specified, it will use the list of referenced assemblies as
determined by the build.

### ApiReferenceOutputDir

The directory where the API Reference source is created.

Defaults to `$(TargetDir)`.

### ApiReferenceOutputExt

The extension used for the API Reference source.

Defaults to `.cs` when the format is C#, and `.cs.md` when the format is
MarkDown-with-C#.

### ApiReferenceOutputPath

The full path of the API Reference source.

Defaults to
`$(ApiReferenceOutputDir)$(TargetName)$(ApiReferenceOutputExt)`.

Note: when setting this yourself in a project file, you cannot rely on
any of the other defaulted properties (like `ApiReferenceOutputExt`,
for example), because that defaulting happens after the project file is
read. To be able to do that, set it in `Directory.Build.targets`
([docs][d.b.t]).

### CreateApiReferenceOutputDir

Determines whether the directory part of `ApiReferenceOutputPath` will
be created as part of the processing.

Defaults to `false`.

### EnableBinaryEnumHandling

Enables special enum handing; if set to `true`, when an enum is marked as
`[Flags]`, binary literals will be used for its values.

Defaults to `false`.

### EnableCharEnumHandling

Enables special enum handing; if set to `true`, when an enum uses
`ushort` as base type and all values are reasonably representable as
characters, then character literals will be used.

Defaults to `false`.

### EnableHexEnumHandling

Enables special enum handing; if set to `true`, when an enum is marked as
`[Flags]` and all values either have a single bit or a single set of
contiguous bits set, hexadecimal literals will be used for its values.

This has no effect when `EnableBinaryEnumHandling` is also set.

Defaults to `false`.

### GenerateApiReference

Determines whether API Reference sources will be generated for each
assembly.

Defaults to `true`.

### MonoRunner

The program used to run the generator under Mono.

Defaults to `mono --runtime=v4.0.30319`.

### NetCoreRunner

The program used to run the generator under .NET Core.

Defaults to `dotnet`.

### SkipApiReferenceOutputPathFileWrite

Determines whether the output files are registered in `@(FileWrites)`.

Defaults to `false`.

## Configuration - Which Attributes to Include

The choice of which attributes to consider part of the public API is
based on two item groups:

| Item Group                   | Description                                                                                                                     |
|------------------------------|---------------------------------------------------------------------------------------------------------------------------------|
| ApiReferenceIncludeAttribute | Attributes to include. If not specified, all attributes are included, unless excluded via `@(ApiReferenceExcludeAttribute)`.    |
| ApiReferenceExcludeAttribute | Attributes to exclude. Applies to attributes included (whether explicitly or implicitly) via `@(ApiReferenceIncludeAttribute)`. |

In both cases, names match against the full internal name of the
attribute type (like ``Namespace.GenericTypeName`2/NestedAttribute``).
Attributes handled as part of syntax generation (like
`System.ParamArrayAttribute` and
`System.Runtime.CompilerServices.ExtensionAttribute`) are never
included. Note that wildcards are not supported (MSBuild would match
those against the file system).

`ApiReferenceExcludeAttribute` has a number of attributes preloaded; you
can list these out using something like this:

```xml
  <Target Name="ListAttributesExcludedFromApiReference">
    <Message Importance="high" Text="The following attributes are configured to be excluded from the generated API reference:" />
    <Message Importance="high" Text="- %(ApiReferenceExcludeAttribute.Identity)" />
  </Target>
```

You can also use the `Remove` option of the `ItemGroup` to remove some
or all of them if you would prefer to retain them; this option _does_
allow wildcards (they are matched against existing items).

For example,

```xml
  <ItemGroup>
    <ApiReferenceExcludeAttribute Remove="System.Reflection.Assembly*Attribute" />
  </ItemGroup>
```

will re-enable all the assembly metadata attributes (like
`[AssemblyProduct]` and `[AssemblyCopyright]`).

## Release Notes

These are available [on GitHub][GHReleases].

## Credits

Package icon created by [DinosoftLabs - FlatIcon][PackageIcon].

[CI-S]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml/badge.svg
[CI-L]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml

[NuGet-S]: https://img.shields.io/nuget/v/Zastai.Build.ApiReference
[NuGet-L]: https://www.nuget.org/packages/Zastai.Build.ApiReference

[GHReleases]: https://github.com/Zastai/Zastai.Build.APIReference/releases
[PackageIcon]: https://www.flaticon.com/free-icon/browser_718064

[d.b.t]: https://docs.microsoft.com/en-us/visualstudio/msbuild/customize-your-build#directorybuildprops-and-directorybuildtargets
