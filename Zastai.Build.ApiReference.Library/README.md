# Zastai.Build.ApiReference.Library [![Build Status][CI-S]][CI-L] [![NuGet Package Version][NuGet-S]][NuGet-L]

This library provides a means of formatting an assembly's public API
as either plain C# or Markdown with C# code blocks.

## Main Use

The main usage pattern is:

- Create an instance of `CodeFormatter`; currently, two implementations
  are provided: `CSharpFormatter` and `CSharpMarkdownFormatter`.
- Configure the processing, using methods like `EnableCharEnums()` and
  `ExcludeCustomAttributes()`, and properties like `IncludeInternals`.
- Call the `FormatPublicApi()` method, which returns a stream of API
  reference source lines, which you are then free to send wherever you
  like.

Note that formatters are _not_ currently thread-safe; only one
`FormatPublicApi()` call should be running at any one time. Note that
this includes the streaming processing triggered by enumerating its
result, so if you want to do multiple calls before processing the
results, you will need to either use multiple instances, or use
something like `ToList()` to make sure all processing is completed
before the next call.

## Implementing Other Output Languages

In and of itself, this can be achieved by creating a new subclass of
`CodeFormatter` and implementing the various abstract methods.

However, it's currently written with C# in mind, so the API may not
be 100% suitable if trying to do a different language.

## Release Notes

These are available [on GitHub][GHReleases].

## Credits

Package icon created by [DinosoftLabs - FlatIcon][PackageIcon].

[CI-S]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml/badge.svg
[CI-L]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml

[NuGet-S]: https://img.shields.io/nuget/v/Zastai.Build.ApiReference.Library
[NuGet-L]: https://www.nuget.org/packages/Zastai.Build.ApiReference.Library

[GHReleases]: https://github.com/Zastai/Zastai.Build.APIReference/releases
[PackageIcon]: https://www.flaticon.com/free-icon/browser_718064
