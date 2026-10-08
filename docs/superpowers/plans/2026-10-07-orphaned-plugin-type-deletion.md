# Orphaned Plugin Type Deletion — Implementation Plan

**Goal:** When a deployed plugin assembly no longer contains a class that Dataverse still has a
`plugintype` record for, remove that orphaned `plugintype` (and the steps hanging off it) as part of
the **assembly** deploy — **automatically**, not gated by a flag, because Dataverse rejects the
`pluginassembly` content update while a stale type remains. `--delete-orphaned` continues to govern
orphan **steps**.

**Out of scope — package mode:** Dataverse already prunes types when a `pluginpackage` is updated
(version bump), so dvx does not own package-mode type cleanup.

> **Resolution update (post-review):** the original design gated this behind `--delete-orphaned`
> and pruned *after* the content update. Manual testing showed the update itself is rejected by
> Dataverse (`PluginType [...] not found in PluginAssembly [...]`), so reconciliation is
> **unconditional** and runs **before** the update — the order spkl uses
> (`UnregisterRemovedPluginTypes` → `Update`).

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
| T1 | Type reconciliation is **unconditional** in assembly mode (spkl-style) and runs **before** the content update. `--delete-orphaned` still governs orphan **steps** only. No new flag. |
| T2 | Pruning is **scoped to the one deployed `pluginassembly` id** — never touch types on any other assembly. |
| T3 | The **desired type set** is the reflection of the built DLL (`PluginDiscovery.DiscoverPluginTypeNames`). |
| T4 | Never delete a type referenced by a **Custom API** (or Custom Action) — mirror the step protection. |
| T5 | Delete an orphan type's **steps first, then the type** (images cascade with the step). |
| T6 | **Skip pruning and warn** when the desired set is empty (guards against a reflection/load failure nuking every type). |
| T7 | Order within an assembly deploy: (1) delete orphan types (existing ∖ desired, minus Custom-API/Action types) → (2) update the content → (3) register new types. An orphan type's steps are deleted with it; `StepRegistrar` then handles steps whose class still exists. |
| T8 | In `--dry-run`, report "would delete" and perform no writes. |
| T9 | `register` stays steps-only (it does not deploy content). |
| T10 | Reconciliation is automatic; no flag can disable it (the update cannot succeed otherwise). |
| T11 | Types are **never** added to a solution (only the assembly and steps are), so pruning leaves no dangling `solutioncomponent`; `AddAssemblyToSolution` keeps `AddRequiredComponents = false`. |

**Alternative considered:** reusing `--delete-orphaned` to gate type deletion (the original design).
Rejected after manual testing — the `pluginassembly` content update is rejected by Dataverse while a
stale type remains, so reconciliation must be unconditional and happen before the update.

---

## Architecture

```
PluginDeployRunner.BuildAndDeploy(svc, mode, solution, …)
  │   build → PluginDeploymentPlan.Resolve → deploy → assemblyId
  │
  └── mode == Assembly → DeployAssembly(...)   (seam; unit-tested in PluginDeployRunnerTests)
        desired = PluginDiscovery.DiscoverPluginTypeNames(build.DllPath)
        1. existingId = AssemblyDeployer.FindExistingId(name)
        2. if existingId: PluginTypeRegistrar.DeleteOrphans(existingId, desired, dryRun)   ← BEFORE update
        3. AssemblyDeployer.Deploy(artifact)                                                (content update)
        4. if !dryRun: PluginTypeRegistrar.EnsureRegistered(assemblyId, desired)
  ▼
SyncCommand: StepRegistrar.Sync(assemblyId, definitions, …, deleteOrphaned)   ← steps only, unchanged
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
- `src/dvx/Services/PluginTypeRegistrar.cs` — add `DeleteOrphans(...)` (used by the assembly deploy).
- `src/dvx/Services/SdkMetadata.cs` — add `plugintypeid` to the `CustomActions` `ColumnSet`
  (`SdkMetadata.cs:93` selects only `workflowid, uniquename` today) and add
  `CustomActionPluginTypeIds()` beside `CustomActionMessageIds()`; reuse `CustomApiPluginTypeIds()`.
- `src/dvx/Services/AssemblyDeployer.cs` — expose `FindExistingId(name)` (refactored from `ResolveExistingId`).
- `src/dvx/Commands/Shared/PluginDeployRunner.cs` — assembly mode reflects the DLL, then calls
  `DeployAssembly(...)` (the internal, unit-tested seam): delete orphan types **before** the content
  update, deploy, register new types after. Package mode is untouched; `deploy` gains no flag.
- `src/dvx/Commands/SyncCommand.cs` — unchanged runner call; its `--delete-orphaned` still governs
  orphan steps via `StepRegistrar`.
- `README.md` — describe automatic assembly-mode type reconciliation; keep `--delete-orphaned` steps-only.

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
- [x] Assembly mode reflects the DLL, then `DeployAssembly(deployer, registrar, artifact, typeNames, dryRun, verbose)`
  — the internal seam that (1) deletes orphan types **before** the content update, (2) deploys, (3) registers new types.
- [x] `PluginDeployRunnerTests` (red first): asserts `Delete(plugintype)` precedes `Update(pluginassembly)`;
  a new assembly deletes nothing; dry-run writes nothing.

### Task 5 — CLI surface
- [x] None needed: reconciliation is automatic, so `deploy` gains no flag and the steps-only
  `DeleteOrphanedSteps()` help is reused unchanged by `sync`/`register`.

### Task 6 — Docs
- [x] README: assembly-mode types are reconciled automatically (removed before the content update);
  `--delete-orphaned` stays steps-only. Reverted the earlier `deploy --delete-orphaned` row.

---

## Edge cases & protections (must all hold)

- **Custom API backing type** — skip (would break the API). Warn.
- **Custom Action backing type** — skip (Task 2: `workflow.plugintypeid`, resolved via `CustomActionPluginTypeIds()`).
- **Renamed class** — new type created (ensure) + old type pruned = clean rename.
- **Ordering** — in an assembly deploy, orphan types are deleted **before** the content update
  (Dataverse rejects the update otherwise); steps whose class still exists are handled by
  `StepRegistrar` afterwards.
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
- `dotnet test` (all green: `PluginTypeRegistrarTests`, `PluginDeployRunnerTests`)
- Manual, against a dev org (assembly mode):
  1. Deploy with classes A, B → 2 `plugintype` rows.
  2. Remove B, redeploy (no flag) → B's type gone; A intact; **the content update succeeds**.
  3. Rename A→C, redeploy → C created, A gone.
  4. Add a `[CustomApi]`/Custom Action class, deploy, remove it, redeploy → its type is kept with a warning.
  5. `--dry-run` → reports would-prune, writes nothing.
  6. Package mode: bump the version → Dataverse removes the stale type itself (no dvx action).

