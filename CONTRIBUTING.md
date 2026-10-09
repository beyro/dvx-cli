# Contributing

Thanks for your interest in dvx. This guide covers how to build, test, and ship changes.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A Dataverse environment if you want to test commands end to end.

## Project layout

- `src/dvx` — the CLI tool (`dvx.cli`), targets `net9.0`
- `src/dvx.PluginAttributes` — the `[PluginStep]` attributes package, multi-targets `net462;net471;net9.0`
- `src/dvx.Tests` — xUnit test project

## Build and test

```powershell
dotnet build
dotnet test
```

Run a single test by name:

```powershell
dotnet test --filter "FullyQualifiedName~PluginDiscoveryTests"
```

Please make sure `dotnet test` passes before opening a pull request — CI runs it on every PR.

## Installing your local build

```powershell
dotnet tool uninstall -g dvx.cli;
dotnet build -c Release;
dotnet tool install -g dvx.cli --source src\dvx\bin\Release --no-cache
```

## Pull requests

- Branch off `main` and target `main`.
- Keep changes focused; one concern per PR.
- Add or update tests for behavior changes.
- CI (`.NET Quality Gate`) runs build, tests, and Sonar analysis — it must pass before merge.

## Releasing

Releases are published to NuGet by CI when a version tag is pushed. Maintainers only.

1. Bump `<Version>` in `src/dvx/dvx.csproj` and update `<PackageReleaseNotes>`.
2. Commit to `main`.
3. Tag and push:

   ```powershell
   git tag v1.12.0
   git push origin v1.12.0
   ```

4. The `Publish to NuGet` workflow builds, tests, packs, and pushes the package.

The workflow fails if the tag does not match `<Version>` in `src/dvx/dvx.csproj`, so bump the version before tagging.
