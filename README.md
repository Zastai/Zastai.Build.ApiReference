# Zastai.Build.ApiReference [![Build Status][CI-S]][CI-L]

This project aids in generating a (public) API reference for a .NET
assembly, in the form of pseudocode (currently only C#) with optional
MarkDown markup.

This output is not intended to be compilable; rather the intent is for
it to be kept under source control, so that any changes to public API
are easy to detect and track.

## Components

This consists of 3 separate packages:

- An [MSBuild task][task] to automatically apply the API extraction as
  part of a build.
- A [library][library] implementing the actual API extraction.
- A [dotnet tool][tool] to run the API extraction manually.

## Release Notes

These are available [on GitHub][GHReleases].

## Credits

Package icon created by [DinosoftLabs - FlatIcon][PackageIcon].

[CI-S]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml/badge.svg
[CI-L]: https://github.com/Zastai/Zastai.Build.APIReference/actions/workflows/build.yml

[task]: Zastai.Build.ApiReference/README.md
[library]: Zastai.Build.ApiReference.Library/README.md
[tool]: Zastai.Build.ApiReference.Tool/README.md

[GHReleases]: https://github.com/Zastai/Zastai.Build.APIReference/releases
[PackageIcon]: https://www.flaticon.com/free-icon/browser_718064
