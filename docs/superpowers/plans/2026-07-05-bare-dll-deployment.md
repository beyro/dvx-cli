# Bare DLL Plugin Deployment — Implementation Plan

**Goal:** Let `dvx plugin deploy` and `dvx plugin sync` deploy a bare plugin assembly
(`pluginassembly.content`) as an alternative to the NuGet plugin-package path
(`pluginpackage.content`), without breaking the existing package workflow.

**Tech Stack:** .NET 8, System.CommandLine, Microsoft.Xrm.Sdk, xUnit, Shouldly, NSubstitute.

## Locked Decisions

| # | Decision |
|---|----------|
| A1–A3 | Explicit flag `--plugin-build-mode package\|assembly` on `deploy` and `sync`; defaults to `package` |
| A4 | Config field `pluginBuildMode` (project-level) |
| A5 | CLI wins over config |
| B6 | Assembly mode still runs `dotnet build` |
| B8 | Distinct build method for assemblies; share code where it makes sense |
| C9 | Create the `pluginassembly` record when none exists |
| C10 | Resolve by `name`; throw if more than one match |
| C11 | Set only required attributes; none configurable |
| C12 | `version` from the assembly's `AssemblyVersion` |
| C13 | `isolationmode` fixed at Sandbox (2) |
| C14 | Add the assembly to the target solution |
| D15 | `IPluginDeployer` with `PackageDeployer` + `AssemblyDeployer` |
| D16 | Shared "resolve record → upload content → return assembly id" skeleton |
| D17 | Dry-run reports "would create" and skips |
| E18 | Warn when mode and produced artifacts disagree |
| E19 | Default stays `package` (backward compatible) |
| E20 | No client-side assembly validation — let Dataverse reject |
| F21–F22 | NSubstitute-based tests mirroring `PackageDeployerTests`; add `ProjectBuilder` tests |
| F23 | Update README |

> **Resolution note (implemented):** package mode is **strict** — a missing `.nupkg` is a hard error,
> not a warning, so the deploy never silently switches to the DLL. The E18 warning therefore applies
> only to assembly mode on a project that also emitted a `.nupkg` (deploy the DLL, ignore the package).

## Architecture

```
DeployCommand / SyncCommand
  │  resolve mode (CLI > config), build artifact, pick deployer
  ▼
IPluginDeployer.Deploy(PluginArtifact, verbose, dryRun) → Guid assemblyId
  ├── PackageDeployer   : pluginpackage.content  → child pluginassembly id
  └── AssemblyDeployer  : pluginassembly.content (create or update) → id
  ▼
StepRegistrar.Sync(assemblyId, …)   ← unchanged
```

**Shared skeleton** (`PluginDeployerBase`): `ResolveExistingId(...)` → `UploadContent(id, path, verbose)`
→ `ReturnAssemblyId(...)`. `PackageDeployer` keeps its package lookup + child-assembly lookup;
`AssemblyDeployer` overrides create/update and returns the record's own id.

## Files

**Create**
- `src/dvx/Models/PluginBuildMode.cs` — `enum PluginBuildMode { Package, Assembly }`
- `src/dvx/Services/IPluginDeployer.cs` — interface + `PluginArtifact` record
- `src/dvx/Services/PluginDeployerBase.cs` — shared skeleton
- `src/dvx/Services/AssemblyDeployer.cs`
- `src/dvx.Tests/AssemblyDeployerTests.cs`
- `src/dvx.Tests/ProjectBuilderTests.cs`

**Modify**
- `src/dvx/Services/PackageDeployer.cs` — implement `IPluginDeployer`, derive from base
- `src/dvx/Services/ProjectBuilder.cs` — add assembly build path (nullable nupkg / distinct method)
- `src/dvx/Models/AppConfig.cs` — add `PluginBuildMode? PluginBuildMode`
- `src/dvx/Config/ConfigLoader.cs` — add `ResolvePluginBuildMode(config, cliOverride)`
- `src/dvx/Commands/Shared/CommandOptions.cs` — add `PluginBuildMode()` option (`--plugin-build-mode`)
- `src/dvx/Commands/DeployCommand.cs` — wire mode + deployer selection
- `src/dvx/Commands/SyncCommand.cs` — same
- `src/dvx/Services/SolutionService.cs` — add `AddAssemblyToSolution` (component type 91)
- `README.md`

## Interfaces

```csharp
public enum PluginBuildMode { Package, Assembly }

public sealed record PluginArtifact(
    string  Path,          // .nupkg (Package) or .dll (Assembly)
    string  AssemblyName,  // bare assembly name; record name for Assembly mode, stem for Package unique name
    string  UniqueName,    // {prefix}_{assemblyName} — Package lookup key
    Version Version);      // from the built DLL's AssemblyVersion (Assembly mode)

public interface IPluginDeployer
{
    Guid Deploy(PluginArtifact artifact, bool verbose = false, bool dryRun = false);
}
```

`AssemblyDeployer` sets required `pluginassembly` attributes:
`name = artifact.AssemblyName` (bare), `sourcetype = 0` (Database), `isolationmode = 2` (Sandbox),
`version = artifact.Version.ToString()`, `culture = "neutral"`, `content = base64(dll)`.
`version` via `AssemblyName.GetAssemblyName(dllPath).Version` (metadata-only; no dependency load).

## Task Breakdown

All tasks follow **TDD**: write the failing test(s) first, run them to confirm they fail, then
implement until green. Task 3 is a pure refactor, so its "red" step is the existing
`PackageDeployerTests` passing before and after.

### Task 1 — `PluginBuildMode` enum + config plumbing
- [x] **Red:** Write `ConfigLoaderTests` for `ResolvePluginBuildMode` (CLI wins, config fallback,
      default Package) and run `dotnet test --filter ConfigLoaderTests` — confirm failure.
- [x] **Green:** Add enum, `AppConfig.PluginBuildMode`, `ConfigLoader.ResolvePluginBuildMode`,
      `CommandOptions.PluginBuildMode()`.
- [x] Run `dotnet test --filter ConfigLoaderTests` — confirm pass.

### Task 2 — `ProjectBuilder` assembly build path
- [x] **Red:** Write `ProjectBuilderTests`: DLL-only project returns artifact without requiring
      `.nupkg`; package project still returns both; missing DLL still throws. Run
      `dotnet test --filter ProjectBuilderTests` — confirm failure.
- [x] **Green:** Add a build method that tolerates a missing `.nupkg` (share `RunDotnet`/`FindBuiltDll`).
- [x] Run `dotnet test --filter ProjectBuilderTests` — confirm pass.

### Task 3 — `IPluginDeployer` + shared base (refactor)
- [x] **Baseline:** Run `dotnet test --filter PackageDeployerTests` — confirm existing tests pass.
- [x] Define interface + `PluginArtifact`.
- [x] Extract shared skeleton; refactor `PackageDeployer` to implement it.
- [x] Re-run `dotnet test --filter PackageDeployerTests` — confirm still pass (no behaviour change).

### Task 4 — `AssemblyDeployer`
- [x] **Red:** Write `AssemblyDeployerTests` (NSubstitute `IOrganizationService`):
      create when absent; update when present; multiple matches throws; dry-run create reports
      "would create" and issues no `Create`/`Update`; returns record id. Run
      `dotnet test --filter AssemblyDeployerTests` — confirm failure.
- [x] **Green:** Implement resolve-by-name (throw on >1), create/update `content`, return id.
- [x] Run `dotnet test --filter AssemblyDeployerTests` — confirm pass.

### Task 5 — Solution membership
- [x] **Red:** Add a test asserting the `AddSolutionComponent` request shape for
      `AddAssemblyToSolution` (component type 91) — confirm failure.
- [x] **Green:** Add `SolutionService.AddAssemblyToSolution` (component type 91).
- [x] Run the solution-service tests — confirm pass.

### Task 6 — Command wiring
- [x] **Red:** Add command-level tests for mode selection (via the extracted `PluginDeploymentPlan`
      seam) — confirm failure.
- [x] **Green:** `DeployCommand` / `SyncCommand`: resolve mode, build accordingly, select deployer,
      warn on artifact/mode mismatch (E18), add assembly to solution when `--solution-unique-name` set.
- [ ] Run the command tests — confirm pass.

### Task 7 — Docs
- [x] README: document `--plugin-build-mode`, `pluginBuildMode` config field, assembly-mode
      create/update semantics, and solution-membership behaviour.

## Verification
- `dotnet build dvx.sln`
- `dotnet test`
- Manual: `dvx plugin deploy --project ./MyPlugin.csproj --plugin-build-mode assembly --dry-run`
  then without `--dry-run` against a dev org; re-run to confirm update path.
