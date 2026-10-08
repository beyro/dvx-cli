# Orphaned Plugin Type Deletion — Implementation Plan

**Goal:** When a deployed plugin assembly no longer contains a class that Dataverse still has a
`plugintype` record for, remove that orphaned `plugintype` — and the steps/images hanging off it —
in **assembly** deployment mode (`--plugin-build-mode assembly`), behind the existing
`--delete-orphaned` flag, with full `--dry-run` support.

**Out of scope — package mode:** Dataverse already prunes types when a `pluginpackage` is updated
(version bump), so dvx does not own package-mode type cleanup.

**Tech Stack:** .NET 9, System.CommandLine, Microsoft.Xrm.Sdk, xUnit, Shouldly, NSubstitute.

---

## Background: why orphans exist

A `plugintype` row is the registration of one plugin class on a `pluginassembly`. It is a *child*
of the assembly and is what steps bind to (`sdkmessageprocessingstep.plugintypeid`).

Neither deployment path reconciles types when a class is **removed or renamed**:

| Mode | Who creates `plugintype` | Who removes a stale one |
|---|---|---|
| `assembly` | dvx itself (`PluginTypeRegistrar`, added in `3509402`) | **nobody** — dvx must |
| `package` | Dataverse, extracting from the `.nupkg` | Dataverse (on package update) — no dvx action |

So only **assembly** mode needs type pruning. Renaming `Foo` → `Bar` and re-deploying leaves the
old `Foo` type (and possibly its steps) in Dataverse forever. `--delete-orphaned` currently only
prunes orphaned **steps** (`StepRegistrar.cs:158-183`) and orphaned **web resources**
(`WebResourceSyncer.cs:176-196`), not types.

Terminology: an **orphaned type** = a `plugintype` whose `pluginassemblyid` is the assembly we just
deployed, but whose `typename` is absent from the built DLL.

---

## Locked Decisions

| # | Decision |
|---|----------|
| T1 | Reuse the existing **`--delete-orphaned`** flag on `sync` **and add it to `deploy`**; extend its meaning to "orphaned registrations" (steps *and* types). Type pruning applies only in **assembly** mode. No new flag. |
| T2 | Pruning is **scoped to the one deployed `pluginassembly` id** — never touch types on any other assembly. |
| T3 | The **desired type set** is always the reflection of the built DLL (`PluginDiscovery.DiscoverPluginTypeNames`), for both modes. |
| T4 | Never delete a type referenced by a **Custom API** (or Custom Action) — mirror the step protection. |
| T5 | Delete an orphan type's **steps first, then the type** (images cascade with the step). |
| T6 | **Skip pruning and warn** when the desired set is empty (guards against a reflection/load failure nuking every type). |
| T7 | Prune **after** ensure-registered and, in `sync`, **before** `StepRegistrar.Sync` — so step reconciliation sees a clean type set. |
| T8 | In `--dry-run`, report "would delete" and perform no writes. |
| T9 | `register` stays steps-only (it does not deploy content); type pruning applies to `deploy`/`sync`. |
| T10 | Default remains off. Destructive — documented as such. |
| T11 | Types are **never** added to a solution (only the assembly and steps are), so pruning leaves no dangling `solutioncomponent`; `AddAssemblyToSolution` keeps `AddRequiredComponents = false`. |

**Alternative considered:** a separate `--delete-orphaned-types`. Rejected — `--delete-orphaned`
already reads as "remove registrations no longer in code"; two adjacent flags invite confusion.
If reviewers prefer an explicit opt-in, T1 is the one decision to revisit.

---

## Architecture

```
PluginDeployRunner.BuildAndDeploy(svc, mode, solution, …, deleteOrphaned)
  │   build → PluginDeploymentPlan.Resolve → deployer.Deploy() → assemblyId
  │
  ├── if mode == Assembly && !dryRun          → PluginTypeRegistrar.EnsureRegistered(assemblyId, desired)
  ├── if mode == Assembly && deleteOrphaned   → PluginTypeRegistrar.DeleteOrphans(assemblyId, desired, dryRun)
  │        └── desired = PluginDiscovery.DiscoverPluginTypeNames(build.DllPath)  (assembly mode only)
  ▼
SyncCommand: StepRegistrar.Sync(assemblyId, definitions, …, deleteOrphaned)   ← unchanged
```

`DeleteOrphans(assemblyId, desired)`:
1. `existing = SdkMetadata.PluginTypeIdByName(assemblyId)`  → `{typename → id}`
2. `protectedTypes = CustomApiPluginTypeIds() ∪ CustomActionPluginTypeIds()`
3. `orphans = existing where !desired.Contains(typename) && !protectedTypes.Contains(id)`
4. for each orphan: delete its `sdkmessageprocessingstep` rows, then `svc.Delete("plugintype", id)`

---

## Files

**Create**
- `src/dvx.Tests/PluginTypeRegistrarTests.cs` — already exists; add the prune cases here.

**Modify**
- `src/dvx/Services/PluginTypeRegistrar.cs` — add `DeleteOrphans(...)`; factor the shared
  "load existing types" query so `EnsureRegistered` and `DeleteOrphans` don't double-query when
  both run in the same deploy.
- `src/dvx/Services/SdkMetadata.cs` — add `plugintypeid` to the `CustomActions` `ColumnSet`
  (`SdkMetadata.cs:93` currently selects only `workflowid, uniquename`) and add
  `CustomActionPluginTypeIds()` beside `CustomActionMessageIds()`; reuse the existing
  `CustomApiPluginTypeIds()`.
- `src/dvx/Commands/Shared/PluginDeployRunner.cs` — accept `bool deleteOrphaned`; in **assembly
  mode** reflect the DLL once and call `EnsureRegistered` (real runs) then, when `deleteOrphaned`,
  `DeleteOrphans`. Package mode is untouched.
- `src/dvx/Commands/DeployCommand.cs` — reuse the existing `--delete-orphaned` option; pass it through.
- `src/dvx/Commands/SyncCommand.cs` — pass the existing `--delete-orphaned` into the runner.
- `src/dvx/Commands/Shared/CommandOptions.cs` — **retitle the existing `DeleteOrphanedSteps()`**
  option (help text covers steps **and** types). Do **not** add a new option: `DeleteOrphaned()`
  (web resources, `CommandOptions.cs:78`) and `DeleteOrphanedSteps()` (`:83`) already share the
  literal `--delete-orphaned` string — a pre-existing duplicate smell worth noting (and, if
  desired, a follow-up cleanup), not something to add a third of.
- `README.md` — document the extended `--delete-orphaned` semantics for deploy/sync.

---

## Interfaces

```csharp
public class PluginTypeRegistrar(IOrganizationService svc)
{
    // existing (assembly-mode create)
    public void EnsureRegistered(Guid assemblyId, IReadOnlyList<string> desiredTypeNames, bool verbose = false);

    // new
    /// <summary>Deletes plugintypes on <paramref name="assemblyId"/> that are absent from the desired
    /// set (and not backing a Custom API/Action), deleting each orphan's steps first. No writes on dry-run.</summary>
    public void DeleteOrphans(Guid assemblyId, IReadOnlyList<string> desiredTypeNames,
                              bool dryRun = false, bool verbose = false);
}
```

---

## Task Breakdown

Every task is TDD: failing test → implement → green. Command wiring has no unit seam (as with the
existing deploy flow) → covered by runner code review + a manual dev-org check.

### Task 1 — `PluginTypeRegistrar.DeleteOrphans`
- [x] **Red:** `PluginTypeRegistrarTests` (NSubstitute `IOrganizationService`):
  deletes a type not in the desired set; keeps one that is; **never** deletes a Custom-API type;
  query is filtered by `pluginassemblyid`; deletes the orphan's `sdkmessageprocessingstep` rows
  before the `plugintype`; no `Delete` on dry-run; empty desired set → no deletes + no throw.
  Run `dotnet test --filter PluginTypeRegistrarTests` — confirm failure.
- [x] **Green:** implement `DeleteOrphans` (query existing + protected ids, resolve orphans,
  delete steps then type).
- [x] Run `dotnet test --filter PluginTypeRegistrarTests` — confirm pass.

### Task 2 — Custom-Action type protection
- [x] **Red:** add `plugintypeid` to the `CustomActions` `ColumnSet` (`SdkMetadata.cs:93` fetches only
  `workflowid, uniquename` today), add `CustomActionPluginTypeIds()`, and extend `PluginTypeRegistrarTests`
  with a Custom-Action-backed type that must **not** be deleted. Confirm failure.
- [x] **Green:** fold `CustomActionPluginTypeIds()` into the protected set used by `DeleteOrphans`.
- [x] Confirm pass. (The `workflow` entity does carry `plugintypeid` for Action definitions — this is
  real work, not a conditional.)

### Task 3 — Empty-desired guard (safety rail)
- [x] **Red:** test: `DeleteOrphans(assemblyId, emptyDesired)` with existing types → no deletes.
- [x] **Green:** return early (+ `Out.Warn`) when `desiredTypeNames.Count == 0`.

### Task 4 — Wire into `PluginDeployRunner`
- [x] Add `bool deleteOrphaned` to `BuildAndDeploy`; in **assembly mode** reflect the DLL once; call
  `EnsureRegistered` (real runs) and, when `deleteOrphaned`, `DeleteOrphans`. Package mode is
  untouched. Keep dry-run read-only.
- [x] Manual smoke: unit build still green (`dotnet build dvx.sln`, `dotnet test`).

### Task 5 — CLI surface
- [x] `CommandOptions`: **retitle** the existing `DeleteOrphanedSteps()` option so its help covers
  steps and plugin types. Reuse it — no new option (see the pre-existing duplicate note above:
  `DeleteOrphaned()` and `DeleteOrphanedSteps()` already share `--delete-orphaned`).
- [x] `DeployCommand`: reuse the `DeleteOrphanedSteps()` option (do not add a third definition);
  pass the value into the runner.
- [x] `SyncCommand`: pass the existing flag into the runner.

### Task 6 — Docs
- [x] README: extend the `--delete-orphaned` rows for `deploy` and `sync`; note types are pruned
  scoped to the deployed assembly, Custom-API types are never removed, and dry-run first.

---

## Edge cases & protections (must all hold)

- **Custom API backing type** — skip (would break the API). Warn.
- **Custom Action backing type** — skip (Task 2: `workflow.plugintypeid`, resolved via `CustomActionPluginTypeIds()`).
- **Renamed class** — new type created (ensure) + old type pruned = clean rename.
- **Ordering in `sync`** — prune before `StepRegistrar.Sync` so `SdkMetadata.PluginTypeIdByName`
  no longer returns the removed types; orphan-type steps are already gone, so `StepRegistrar`'s
  own orphan-step pass only handles steps whose class still exists.
- **Steps on the orphan type that are Custom-API-protected** — protected by type-protection, so
  they survive; consistent with `StepRegistrar`.
- **Empty desired set / reflection failure** — reflection failures throw (fail closed, no deletes);
  empty desired set skips with a warning (T6).
- **Prune scope** — only the deployed assembly id is touched; other assemblies' types are never candidates (T2).
- **Types & solutions** — types are never added to a solution (only the assembly and steps are, T11),
  so pruning cannot leave a dangling `solutioncomponent`.
- **Dry-run** — no writes; "would delete N orphaned type(s)".

---

## Verification

- `dotnet build dvx.sln` (0 warnings)
- `dotnet test` (all green, new `PluginTypeRegistrarTests` cases included)
- Manual, against a dev org:
  1. Deploy assembly mode with classes A, B → 2 `plugintype` rows.
  2. Remove B, redeploy with `--delete-orphaned` → B's type gone; A intact.
  3. Rename A→C, redeploy with `--delete-orphaned` → C created, A gone.
  4. Add a `[CustomApi]` class, deploy, remove it, redeploy with `--delete-orphaned` → its type is
     **kept** with a warning.
  5. `--dry-run` with `--delete-orphaned` → reports the deletions, writes nothing.
  6. Package mode: bump the package version and confirm Dataverse removes the stale type itself (no dvx action).

