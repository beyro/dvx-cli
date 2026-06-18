# dvx — Manual Regression Test Cases

Covers all commands (`sync`, `deploy`, `register`, `adopt`) and their supporting subsystems.  
Run against a real Dataverse environment. Mark each test **PASS / FAIL / SKIP** with date and tester initials.

---

## Contents

- [CFG — Configuration](#cfg--configuration)
- [BLD — Build](#bld--build)
- [DEP — Deploy](#dep--deploy)
- [REG — Register](#reg--register)
- [SYN — Sync](#syn--sync)
- [ADO — Adopt](#ado--adopt)
- [STP — Step attributes](#stp--step-attributes)
- [IMG — Images](#img--images)
- [IMP — Impersonation (RunAsSystem / RunAsUser)](#imp--impersonation)
- [DRY — Dry run](#dry--dry-run)
- [VRB — Verbose flag](#vrb--verbose-flag)
- [ERR — Error handling and exit codes](#err--error-handling-and-exit-codes)

---

## CFG — Configuration

| ID | Description | Steps | Expected result |
|---|---|---|---|
| CFG-01 | Default config discovery | Run any command **without** `--config` from a project directory containing `dvx.json`. | Tool loads config without error. |
| CFG-01b | Upward config search | Place `dvx.json` in the project root; run `dvx wr sync` **without** `--config` from a nested subdirectory (e.g. `./WebResources`). | Tool finds and loads the root `dvx.json`, **and** the command behaves identically to running from the root: relative config paths (`webResources.folder`, `webResources.manifest`, `project`) resolve against the config file's directory, so the sync completes. |
| CFG-02 | `--config` override | Pass `--config ./custom/config.json` pointing to a valid file in a non-default location. | Tool loads the specified file; default path is ignored. |
| CFG-03 | Config file not found | Pass `--config ./does-not-exist.json`. | Exit 1. Error message includes the missing path and a suggestion to use `--config`. |
| CFG-04 | Config file is empty | Create a zero-byte config file; pass it via `--config`. | Exit 1. Error message says file is empty or invalid. |
| CFG-05 | Config file has invalid JSON | Write `{ bad json` to a file; pass it via `--config`. | Exit 1. Error surfaces a JSON parse failure. |
| CFG-06 | Valid multi-environment config | Config has `dev`, `uat`, `prod` environments. Run `--env uat`. | Tool connects to the `uat` URL, not `dev` or `prod`. |
| CFG-07 | Environment name is case-insensitive | Config defines `"name": "Dev"`. Run with `--env dev` (lowercase). | Tool finds the environment. |
| CFG-08 | Unknown environment name | Run `--env nonexistent`. | Exit 1. Error message includes `"nonexistent"` and lists the available environment names. |
| CFG-09 | `publisherPrefix` from config | Config has `"publisherPrefix": "solu"`. Run `sync` or `deploy` without `--publisher-prefix`. | Tool uses `solu` as the prefix. |
| CFG-10 | `--publisher-prefix` CLI overrides config | Config has `"publisherPrefix": "solu"`. Run with `--publisher-prefix abc`. | Tool uses `abc`, ignoring the config value. |
| CFG-11 | Publisher prefix missing entirely | Config has no `publisherPrefix`; no `--publisher-prefix` passed. Run `sync`. | Exit 1. Error message mentions `publisherPrefix` and `--publisher-prefix`. |
| CFG-12 | `DVX_CLIENT_SECRET` env var | Set `$env:DVX_CLIENT_SECRET = "my-secret"`. Remove `clientSecret` from config (or set it to a wrong value). | Tool authenticates successfully using the env var value. |

---

## BLD — Build

| ID | Description | Steps | Expected result |
|---|---|---|---|
| BLD-01 | Valid project builds and emits nupkg | Run `sync` or `deploy` against a valid `pac plugin init` project. | Output shows `Built <ProjectName>.<version>.nupkg`. Build succeeds. |
| BLD-02 | nupkg path prefers project-name match | Project has multiple `.nupkg` files under `bin/Release`. | Tool picks the one whose stem starts with the project name. |
| BLD-03 | `.csproj` path not found | Pass `--project ./nonexistent.csproj`. | Exit 1. Error message includes the missing path. |
| BLD-04 | `dotnet build` compilation failure | Introduce a compile error in the plugin source. Run `sync`. | Exit 1. Error output includes the `dotnet build` failure details. |
| BLD-05 | No `.nupkg` produced | Modify the `.csproj` so it does not emit a NuGet package. Run `sync`. | Exit 1. Error message says no `.nupkg` found under `bin/Release` and mentions `pac plugin init`. |
| BLD-06 | DLL not found after build | Rename the assembly output in `.csproj` so the DLL name doesn't match the project name. Run `sync`. | Exit 1. Error message says the expected `.dll` was not found. |

---

## DEP — Deploy

| ID | Description | Steps | Expected result |
|---|---|---|---|
| DEP-01 | Happy path — package exists, upload succeeds | Run `deploy` against an environment where the `pluginpackage` record already exists. | Output shows the `Uploading package content...` sub-step and `Deployed. Assembly ID: {id}`. Exit 0. |
| DEP-02 | Plugin package not found in Dataverse | Run `deploy` when no `pluginpackage` record matching `{prefix}_{assemblyName}` exists. | Exit 1. Error message includes the unique name and says "initial upload must be done once manually". |
| DEP-03 | Correct unique name formed | Config has `publisherPrefix = "solu"`. Project name is `MyPlugin`. | The tool looks up `solu_MyPlugin` in Dataverse. Verify via `--verbose` output or error message. |
| DEP-04 | Connection failure — bad credentials | Set an incorrect `clientSecret` in config. Run `deploy`. | Exit 1. Error message references the failed Dataverse connection. With `--verbose`, inner exception details are visible. |
| DEP-05 | Upload rejected by Dataverse | Run `deploy` where the SDK `Update` fails (e.g. revoke the service principal's write privilege, or upload corrupt package content). | Exit 1. Error surfaces the `OrganizationServiceFault` message. |
| DEP-06 | pluginassembly child record not found after upload | Dataverse processed the upload but has no child `pluginassembly` record (rare edge case). | Exit 1. Error message mentions no `pluginassembly` child found and says to ensure initial package upload was fully processed. |

---

## REG — Register

| ID | Description | Steps | Expected result |
|---|---|---|---|
| REG-01 | `--project` path — happy path | Run `register --env dev --project ./MyPlugin.csproj`. Assembly exists in Dataverse. | Builds project locally, finds assembly by name, runs step sync. Exit 0. |
| REG-02 | `--assembly-name` path — happy path | Deploy assembly first. Run `register --env dev --assembly-name MyPlugin`. | Downloads DLL from `pluginassembly.content`, runs step sync. Exit 0. |
| REG-03 | Neither `--project` nor `--assembly-name` | Run `register --env dev` without either option. | Exit 1. Error: "Provide either --project or --assembly-name." |
| REG-04 | Both `--project` and `--assembly-name` | Pass both options together. | Exit 1. Error: "--project and --assembly-name are mutually exclusive." |
| REG-05 | `--assembly-name` — assembly not in Dataverse | Pass a name that doesn't exist. | Exit 1. Error includes the assembly name and suggests running `dvx plugin deploy` first. |
| REG-06 | `--assembly-name` — sourcetype is not Database | Assembly was uploaded with `sourcetype != 0`. | Exit 1. Error says content bytes are not stored in Dataverse and suggests using `--project` instead. |
| REG-07 | `--assembly-name` — content field is empty | `pluginassembly.content` is null/empty. | Exit 1. Error says assembly has no content and suggests using `--project` instead. |

---

## SYN — Sync

| ID | Description | Steps | Expected result |
|---|---|---|---|
| SYN-01 | Full happy path — first registration | Deploy package, then run `sync` with at least one `[PluginStep]`-decorated class. No existing steps. | Build succeeds, package pushed, step(s) created. Output shows `Created: N`. Exit 0. |
| SYN-02 | Re-sync with no changes | Run `sync` twice with identical code. | Second run shows `Skipped: N` (all existing steps already match). No new steps created, no steps deleted. |
| SYN-03 | Step property updated in-place | Change `ExecutionOrder`, `Description`, `FilteringAttributes`, or `RunAsSystem` on an existing `[PluginStep]`. Re-run `sync`. | Output shows `Updated: 1`. The step GUID in Dataverse is unchanged. The new property value is visible in PRT. |
| SYN-04 | Step name change causes delete + create | Change `Entity`, `Message`, `Stage`, or `Async` on a `[PluginStep]`. Re-run `sync`. | Output shows `Deleted: 1, Created: 1`. Old step is removed, new step created with new name. |
| SYN-05 | Orphan step deleted | Remove a `[PluginStep]` attribute from a class. Re-run `sync`. | Output shows `Deleted: 1`. Step no longer exists in Dataverse. |
| SYN-06 | Entire plugin class removed | Delete a plugin class from the DLL. Re-run `sync`. | All steps belonging to that class are deleted. |
| SYN-07 | IPlugin class with no `[PluginStep]` | Add a class implementing `IPlugin` with no attribute. Run `sync`. | Warning logged for the unattributed class. All other steps processed normally. Exit 0 (not exit 2). |
| SYN-08 | Multiple `[PluginStep]` on one class | Decorate one class with two attributes (different entity/message). Run `sync`. | Two separate steps created in Dataverse, both with the correct class GUID. |
| SYN-09 | No `plugintype` records found | Deploy assembly, then run `register` before Dataverse has processed the pluginpackage. | Exit 2. Error in output: "No plugintype records found for assembly…". No Create calls made. |
| SYN-10 | Unknown SDK message | Use `[PluginStep("account", "NonExistentMessage", Stage.PostOperation)]`. Run `sync`. | Warning: "Unknown SDK message 'NonExistentMessage'". Step skipped. Other steps processed. |
| SYN-11 | Entity+message not filterable | Use a combination that has no `sdkmessagefilter` record in Dataverse (with an `Entity` set). | Warning: "No sdkmessagefilter for entity … + message …". Step skipped. |
| SYN-14 | Entity-less (global) message | Register `[PluginStep(Message = "Associate", Stage = Stage.PostOperation)]` (no `Entity`). Run `sync`. | Step **created** with `sdkmessagefilterid` empty. No "No sdkmessagefilter" warning. Verify in PRT the step targets the Associate message with no primary entity. |
| SYN-13 | Partial failure — some steps fail | Simulate by revoking write access to one entity. | Exit 2. Successful steps show in Created/Updated counts. Failed step shown in Errors. |
| SYN-15 | `[CustomApi]` class skipped silently | Add a class implementing `IPlugin` marked `[CustomApi]` (a Custom API implementation, no `[PluginStep]`). Run `sync` (or `register`). | The `[CustomApi]` class is skipped with **no** "no `[PluginStep]`" warning (contrast SYN-07). All other steps processed normally. Exit 0. |

---

## ADO — Adopt

> Run against an assembly that already has steps registered in Dataverse but **no** `[PluginStep]`
> attributes in the source.

| ID | Description | Steps | Expected result |
|---|---|---|---|
| ADO-01 | Happy path — scaffold attributes | Run `adopt --project <csproj>` against an assembly with hand-registered steps. | `[PluginStep(...)]` attributes written onto the matching classes; `using beyro.PluginAttributes;` added where missing. Summary `Adopted: N attribute(s)…`. Exit 0. |
| ADO-02 | Dry run writes nothing | Run `adopt … --dry-run`. | Planned attributes are printed; **no** source files are modified (`git status` clean). |
| ADO-03 | Idempotent re-run | Run `adopt` twice. | Second run reports the steps as already present (skipped); no further file edits. |
| ADO-04 | Unmatched type reported | Ensure a registered step's class is absent/renamed in the project. Run `adopt`. | Warning `No source class found for '<Type>' — skipped.` Other classes still written. Exit 0. |
| ADO-05 | Custom image alias preserved | Adopt a step whose pre/post image uses a non-default alias. | Generated attribute includes `PreImageAlias = "<alias>"` / `PostImageAlias = "<alias>"`. |
| ADO-06 | Unsecure configuration imported | Adopt a step that has an unsecure `configuration` string. | Generated attribute includes `Configuration = "<value>"`. |
| ADO-08 | Round-trip — no churn on first sync | After `adopt`, run `sync --dry-run`. | Output shows `Updated: N`, **`Created: 0`, `Deleted: 0`** — existing steps adopted in place by identity. |
| ADO-09 | Assembly-name override | Run `adopt --assembly-name <name>` where the Dataverse assembly name differs from the project name. | Tool reads steps from the named assembly. |
| ADO-10 | Assembly not found | Run `adopt --assembly-name does-not-exist`. | Exit 1. Error message names the missing assembly. |
| ADO-11 | Entity-less (global) message adopted | Adopt an assembly with a hand-registered `Associate` / `Disassociate` step (no `sdkmessagefilter`). | Generated attribute has an empty entity, e.g. `[PluginStep("", "Associate", Stage.PostOperation)]`. **No** "no entity / not representable" warning. Step is adopted, not skipped. |
| ADO-12 | Custom API not scaffolded; class marked `[CustomApi]` | Adopt an assembly that backs a **Custom API** (plugin bound via `customapi.plugintypeid`; its step is registered at stage 30 / Main Operation). | **No** `[PluginStep]` is written for the Custom API class; the class is marked `[CustomApi]` instead (with `using dvx.PluginAttributes;`). Summary reports `N Custom API class(es) marked [CustomApi]`. Standard event-plugin steps in the same assembly are still adopted. Exit 0. |
| ADO-13 | Custom API dry run | Run the ADO-12 scenario with `--dry-run`. | Planned output shows `… ← [CustomApi]` for the Custom API class and **no** stage-30 `[PluginStep]` / `(Stage)30`. No source files modified (`git status` clean). |
| ADO-14 | Custom API marker idempotent | Run `adopt` twice against the Custom API assembly. | Second run does not add a duplicate `[CustomApi]` (reported as already present / skipped). No further file edits. |

---

## STP — Step attributes

| ID | Description | Steps | Expected result |
|---|---|---|---|
| STP-01 | Step name format | Register a step and inspect the name in PRT. | Name is `TypeFullName \| entity \| message \| StageName \| sync/async`. No `[dvx]` prefix. No integer stage. |
| STP-02 | Stage displayed as text | Use each of the three stages (10, 20, 40). | Step names end with `\| PreValidation \| ...`, `\| PreOperation \| ...`, `\| PostOperation \| ...` respectively. |
| STP-03 | Async mode in step name | Set `Async = true`. Run `sync`. | Step name ends with `\| async`. |
| STP-04 | Sync mode in step name (default) | Omit `Async` (defaults to false). Run `sync`. | Step name ends with `\| sync`. |
| STP-05 | `ExecutionOrder` / rank | Set `ExecutionOrder = 3`. Run `sync`. Inspect step in PRT. | Step `rank` field is `3`. |
| STP-06 | `Description` | Set `Description = "My test step"`. Run `sync`. | Step `description` field shows `"My test step"`. |
| STP-07 | `FilteringAttributes` populated | Set `FilteringAttributes = new[] { "name", "telephone1" }`. Run `sync` for an Update step. | Step `filteringattributes` field contains `"name,telephone1"`. |
| STP-08 | `FilteringAttributes` empty (default) | Omit `FilteringAttributes`. Run `sync`. | Step `filteringattributes` field is null/empty. Step fires on any field change. |
| STP-09 | `FilteringAttributes` cleared | Previously set `FilteringAttributes`; remove them in code and re-sync. | Step `filteringattributes` is cleared. |
| STP-10 | Entity and message lowercased in name | Use `Entity = "Account"` (mixed case) and `Message = "UPDATE"` (upper). | Step name contains `\| account \| update \|`. |
| STP-11 | `Configuration` (unsecure) | Set `Configuration = "<x/>"`. Run `sync`. Inspect step in PRT. | Step `Unsecure Configuration` field shows `<x/>`. Passed to plugin constructor's first parameter. |

---

## IMG — Images

| ID | Description | Steps | Expected result |
|---|---|---|---|
| IMG-01 | Pre-image registered | Set `UsePreImage = true` on a PostOperation step. Run `sync`. | One `sdkmessageprocessingstepimage` record created with `imagetype=0` (Pre), `entityalias="PreImage"`. |
| IMG-02 | Pre-image with specific attributes | Set `PreImageAttributes = new[] { "name", "telephone1" }`. Run `sync`. | Pre-image `attributes` field contains `"name,telephone1"`. |
| IMG-03 | Pre-image with no attributes (all fields) | Set `UsePreImage = true` but omit `PreImageAttributes`. Run `sync`. | Pre-image `attributes` field is null/empty (all attributes included). |
| IMG-04 | Post-image on PostOperation | Set `UsePostImage = true` on a `Stage.PostOperation` step. Run `sync`. | One post-image record created with `imagetype=1`, `entityalias="PostImage"`. |
| IMG-05 | Post-image on non-PostOperation | Set `UsePostImage = true` on a `Stage.PreOperation` step. Run `sync`. | Warning logged: "PostImage … will be skipped — only valid on PostOperation." No image record created. Exit 0. |
| IMG-06 | Pre- and post-image both | Set both `UsePreImage = true` and `UsePostImage = true` on a PostOperation step. | Two image records created — one Pre, one Post. |
| IMG-07 | Image updated after attributes change | Change `PreImageAttributes` and re-run `sync`. | Existing image record updated. Image GUID unchanged. New attributes reflected in Dataverse. |
| IMG-08 | Image removed when flag cleared | Set `UsePreImage = true`, sync. Then set `UsePreImage = false`, sync again. | Pre-image record deleted. Step record itself is updated (not recreated). |
| IMG-09 | Image alias reflects `PreImageAlias` / `PostImageAlias` | Register steps with default and custom aliases. Inspect in PRT. | `entityalias` equals the attribute's `PreImageAlias` / `PostImageAlias`; it defaults to `"PreImage"` / `"PostImage"` only when unspecified (custom aliases are preserved — consistent with ADO-05). |

---

## IMP — Impersonation

| ID | Description | Steps | Expected result |
|---|---|---|---|
| IMP-01 | Neither RunAsSystem nor RunAsUser (default) | Omit both properties. Run `sync`. | `impersonatinguserid` field on step is null. Step runs in calling user's context. |
| IMP-02 | `RunAsSystem = true` | Set `RunAsSystem = true`. Run `sync`. | `impersonatinguserid` is set to the GUID of the SYSTEM user (fetched from Dataverse). Step runs as SYSTEM user. Verify in PRT. |
| IMP-03 | `RunAsUser` with valid GUID | Set `RunAsUser = "<valid-user-guid>"`. Run `sync`. | `impersonatinguserid` is an EntityReference to that user's GUID. Verify in PRT. |
| IMP-04 | `RunAsUser` with invalid string | Set `RunAsUser = "not-a-guid"`. Run `sync`. | Exit 1. Error message includes `"not a valid GUID"` and the provided value. |
| IMP-05 | Both `RunAsSystem` and `RunAsUser` set | Set both `RunAsSystem = true` and `RunAsUser = "<guid>"`. Run `sync`. | Exit 1. Error message includes `"cannot both be set"`. |
| IMP-06 | `RunAsSystem` cleared | Previously synced with `RunAsSystem = true`. Remove `RunAsSystem` from attribute. Re-run `sync`. | `impersonatinguserid` cleared on existing step. Step reverts to calling user context. |
| IMP-07 | `RunAsUser` changed to different user | Update `RunAsUser` to a different valid GUID. Re-run `sync`. | Step updated in-place (same step GUID). `impersonatinguserid` now reflects the new user. |
| IMP-08 | `RunAsUser` cleared | Previously synced with `RunAsUser = "<guid>"`. Remove `RunAsUser` from attribute. Re-run `sync`. | `impersonatinguserid` cleared on existing step. Step reverts to calling user context. |

---

## DRY — Dry run

| ID | Description | Steps | Expected result |
|---|---|---|---|
| DRY-01 | `sync --dry-run` reports correct counts | Run `sync --dry-run` when steps would be created. | Output shows `Created: N (dry run)`. No steps or images created in Dataverse. |
| DRY-02 | `sync --dry-run` reports updates | Pre-existing step with changed property. Run `sync --dry-run`. | Output shows `Updated: N (dry run)`. No `Update` calls made in Dataverse. |
| DRY-03 | `sync --dry-run` reports deletes | Remove a `[PluginStep]` from code. Run `sync --dry-run`. | Output shows `Deleted: N (dry run)`. Step still exists in Dataverse after the run. |
| DRY-04 | `register --dry-run` | Run `register --dry-run`. | Same behaviour as SYN dry-run tests; step sync runs read-only. |
| DRY-05 | Dry-run + verbose together | Run `sync --dry-run --verbose`. | Verbose output is printed (reflection details, step names). No writes to Dataverse. |

---

## VRB — Verbose flag

| ID | Description | Steps | Expected result |
|---|---|---|---|
| VRB-01 | Upload detail printed | Run `sync --verbose` or `deploy --verbose`. | The `Uploading <N> KB to pluginpackage {id}` detail line is printed alongside the `Uploading package content...` sub-step. |
| VRB-02 | Reflection details shown during discovery | Run `sync --verbose`. | For each IPlugin type, its name and `[PluginStep]` arguments are printed, including field name, value, and CLR type. |
| VRB-03 | Inner exception chain shown on error | Trigger a Dataverse SDK error with `--verbose`. | Inner exception messages are printed with `↳ [ExceptionType]` indentation. |
| VRB-04 | No verbose output without flag | Run `sync` without `--verbose`. | No upload detail lines, no reflection details printed. Only step/success summary lines. |

---

## ERR — Error handling and exit codes

| ID | Description | Steps | Expected result |
|---|---|---|---|
| ERR-01 | Exit 0 on full success | Run `sync` with all steps successfully created/updated. | Process exits with code `0`. Verify with `echo $LASTEXITCODE` (PS) or `echo $?` (bash). |
| ERR-02 | Exit 1 on fatal error | Provide an invalid config path. | Process exits with code `1`. |
| ERR-03 | Exit 2 on partial step failure | Simulate by having one step fail while others succeed (e.g. invalid entity name causing a Dataverse error). | Process exits with code `2`. Created/Updated count reflects successful steps. Errors listed in output. |
| ERR-04 | Missing required option `--env` | Run `sync` without `--env`. | System.CommandLine prints usage help and exits non-zero. No Dataverse connection attempted. |
| ERR-05 | Missing required option `--project` (sync/deploy) | Run `sync` without `--project`. | Usage help printed. No Dataverse connection attempted. |
| ERR-06 | Dataverse auth failure | Use an incorrect `clientId` or `clientSecret`. | Exit 1. Error message references the authentication failure. No step operations attempted. |
| ERR-07 | Error message always printed without verbose | Trigger any error without `--verbose`. | `Error: <message>` is printed to the console. No stack trace or inner exceptions shown. |
| ERR-08 | Stack trace / inner exceptions only with verbose | Same error with `--verbose`. | Inner exception chain printed below the primary error message. |
