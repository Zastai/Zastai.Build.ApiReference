# dotnet-api-reference [![Build Status][CI-S]][CI-L] [![NuGet Package Version][NuGet-S]][NuGet-L]

This is a simple command-line tool to enable generating an API reference
source for a .NET assembly.

## Installation

### Local to a Solution

To install the tool as part of a specific solution, run

```pwsh
dotnet tool install dotnet-api-reference --create-manifest-if-needed
```

from the solution folder. This will create a tool manifest
(`.config/dotnet-tools.json`) if needed and make the tool available
using either `dotnet tool run dotnet-api-reference` or
`dotnet dotnet-api-reference`.

When committed, the tool manifest allows using `dotnet tool restore` to
(re)install any tools the solution has configured.

### Machine Wide

To install it globally, for use anywhere on the machine, run

```pwsh
dotnet tool install -g dotnet-api-reference
```

This will enable running the tool using just `dotnet-api-reference`.

## Usage

```pwsh
dotnet-api-reference ASSEMBLY OUTPUT-FILE [OPTIONS]
```

### Options

#### `-ea ATTRIBUTE-TYPE-NAME`

Exclude a particular attribute by name. Simple shell wildcards (`*` and
`?`) are supported.

Names match against the full internal name of the attribute type (like
``Namespace.GenericTypeName`2/NestedAttribute``).

Attributes handled as part of syntax generation (like
`System.ParamArrayAttribute` and
`System.Runtime.CompilerServices.ExtensionAttribute`) are never
included.

#### `-eh ENUM-HANDLING`

Activate specific enum handling (one or more flags, comma-separated).

| Flag     | Description                                                   |
|:---------|:--------------------------------------------------------------|
| `binary` | use binary literals for `[Flags]` enum values                 |
| `char`   | try to use character literals (enums based on `ushort` only)  |
| `hex`    | use hexadecimal literals for `[Flags]` enum values            |

#### `-f FORMAT`

Specifies the format for the API reference source.

| `FORMAT` Value              | Description                  |
|:----------------------------|:-----------------------------|
| `csharp` / `cs`             | plain C# syntax              |
| `csharp-markdown` / `cs-md` | Markdown with C# code blocks |

If not specified, plain C# will be used.

#### `-ia ATTRIBUTE-TYPE-NAME`

Include a particular attribute by name. Simple shell wildcards (`*` and
`?`) are supported. If this is specified, all attributes not explicitly
included will be excluded by default.

Names match against the full internal name of the attribute type (like
``Namespace.GenericTypeName`2/NestedAttribute``).

Attributes handled as part of syntax generation (like
`System.ParamArrayAttribute` and
`System.Runtime.CompilerServices.ExtensionAttribute`) are never
included.

#### `-r DEPENDENCY-DIR`

Adds `DEPENDENCY-DIR` to the set of locations that will be searched
when resolving the main assembly's references.

#### `-v VISIBILITY`

Determines which items are included in the API reference source. Can be
either `public`, to only include public API, or `internal`, to also
include elements marked as `internal` or `private protected`.

If not specified, only public API is included.

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
