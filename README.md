# dvx

A CLI for deploying **code-first Dataverse / Power Platform artifacts** from source or CI/CD.
Commands are grouped by artifact type:

- **`dvx plugin …`** — decorate plugin classes with `[PluginStep]` and let dvx build the package,
  upload it to Dataverse, and keep SDK Message Processing Step records in sync with your code.
- **`dvx webresource …`** — upsert and publish JS/CSS/HTML and other web resources from a folder
  and/or a manifest, optionally pruning ones that have been removed from source.
- **`dvx config …`** — scaffold the `dvx.json` configuration file.

---

## Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Decorating plugins with attributes](#decorating-plugins-with-attributes)
- [Commands](#commands)
  - [plugin sync](#plugin-sync)
  - [plugin deploy](#plugin-deploy)
  - [plugin register](#plugin-register)
  - [plugin adopt](#plugin-adopt)
  - [webresource sync](#webresource-sync)
  - [config create](#config-create)
- [Web resources](#web-resources)
- [Adopting an existing project](#adopting-an-existing-project)
- [How step registration works](#how-step-registration-works)
- [Pre- and post-images](#pre--and-post-images)
- [Exit codes](#exit-codes)
- [Dataverse tables used](#dataverse-tables-used)
- [Project structure](#project-structure)

---

## Requirements

| Requirement | Notes |
|---|---|
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | Runtime for dvx itself |
| Dataverse service principal | ClientId + ClientSecret with the **Dynamics CRM System Administrator** or other role with privileges allowing plugin / web-resource deployment |


---

## Installation

### From a NuGet feed

```
dotnet tool install --global dvx --add-source <your-feed-url>
```

### From source

```
cd src/dvx
dotnet pack
dotnet tool install --global --add-source ./bin/Debug dvx
```

Verify the install:

```
dvx --version
```

---

## Quick start

### Option A — Developer local (config file with default environment)

**1. Generate a config file** in your plugin repo root and fill in the values:

```
dvx config create
```

This creates `dvx.json` in the current directory:

```json
{
  "defaultEnvironment": "dev",
  "environments": [
    {
      "name": "dev",
      "url": "https://your-org.crm4.dynamics.com",
      "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "clientSecret": "your-secret"
    }
  ],
  "publisherPrefix": "yourprefix"
}
```

**2. Reference the attributes package** in your plugin project:

```xml
<PackageReference Include="beyro.PluginAttributes" Version="1.0.0" />
```

**3. Decorate a plugin class:**

```csharp
using beyro.PluginAttributes;

[PluginStep("account", "Create", Stage.PostOperation)]
public class AccountOnPostCreate : IPlugin
{
    public void Execute(IServiceProvider serviceProvider) { ... }
}
```

**4. Build, deploy, and register in one command** (no `--env` needed when `defaultEnvironment` is set):

```
dvx plugin sync --project ./src/MyPlugin/MyPlugin.csproj
```

### Option B — CI/CD pipeline (no config file)

Pass all connection details as options or environment variables. No config file required:

```bash
# Via environment variables (recommended for secrets)
export DVX_URL=https://your-org.crm4.dynamics.com
export DVX_CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
export DVX_CLIENT_SECRET=${{ secrets.DATAVERSE_SECRET }}

dvx plugin sync --publisher-prefix yourprefix --project ./src/MyPlugin/MyPlugin.csproj
```

Or pass everything directly as CLI options:

```
dvx plugin sync \
  --url https://your-org.crm4.dynamics.com \
  --client-id  xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx \
  --client-secret <secret> \
  --publisher-prefix yourprefix \
  --project ./src/MyPlugin/MyPlugin.csproj
```

---

## Configuration

### Config file discovery

When `--config` is not specified, dvx looks for **`dvx.json`** starting in the current working
directory and walking up through parent directories to the filesystem root — like git discovering
`.git` — so commands work from anywhere inside a project. The config is project-local and safe to
check in (omit `clientSecret`; use `DVX_CLIENT_SECRET` instead).

Relative paths **inside the config file** (`project`, `webResources.folder`,
`webResources.manifest`) resolve against the config file's directory — not the directory you run
dvx from — so they keep working from any subdirectory. Paths passed **on the command line**
(`--project`, `--folder`, `--manifest`, `--config`) resolve against the current directory, as
usual for CLI tools.

If no file is found, connection details must be supplied entirely via CLI options or environment variables.

### Full schema

```json
{
  "defaultEnvironment": "dev",
  "environments": [
    {
      "name": "dev",
      "url": "https://your-dev-org.crm4.dynamics.com",
      "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "clientSecret": "your-secret"
    },
    {
      "name": "uat",
      "url": "https://your-uat-org.crm4.dynamics.com",
      "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
      "clientSecret": "your-uat-secret"
    }
  ],
  "publisherPrefix": "yourprefix",
  "solutionUniqueName": "MySolution",
  "webResources": {
    "folder": "./WebResources",
    "manifest": "./webresources.json",
    "publish": true
  }
}
```

| Field | Required | Description |
|---|---|---|
| `defaultEnvironment` | | Name of the environment to use when `--env` is not passed on the command line |
| `name` | ✓ (per env) | Environment alias used with `--env` |
| `url` | ✓ (per env) | Dataverse org URL |
| `clientId` | ✓ (per env) | App registration client ID |
| `clientSecret` | ✓ (per env) | App registration secret |
| `publisherPrefix` | when no solution given | Dataverse publisher customization prefix (e.g. `"beyro"`). Used to form the `pluginpackage` unique name (`{prefix}_{assemblyName}`) and to prefix folder-derived web-resource names. **Fallback only** — when a solution is provided, its publisher's prefix is used instead (and this value, if also set, is ignored with a warning). Can be supplied via `--publisher-prefix`. |
| `solutionUniqueName` | | Unique name of the Dataverse solution to add deployed components (plugin steps / web resources) to. **Authoritative for the customization prefix**: when set, the prefix is read from this solution's publisher rather than `publisherPrefix`. Can be overridden per-command with `--solution-unique-name`. |
| `webResources` | | Defaults for `webresource sync`: `folder`, `manifest`, and `publish` (default `true`). See [Web resources](#web-resources). |

### Connection value resolution

For every connection value, dvx resolves in this priority order (highest wins):

| Value | CLI option | Environment variable | Config file |
|---|---|---|---|
| Environment URL | `--url` | `DVX_URL` | named env entry |
| Client ID | `--client-id` | `DVX_CLIENT_ID` | named env entry |
| Client Secret | `--client-secret` | `DVX_CLIENT_SECRET` | named env entry |

This lets you keep non-secret values in `dvx.json` and inject secrets at runtime:

```json
{
  "defaultEnvironment": "dev",
  "environments": [
    {
      "name": "dev",
      "url": "https://your-org.crm4.dynamics.com",
      "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
    }
  ],
  "publisherPrefix": "yourprefix"
}
```

```bash
export DVX_CLIENT_SECRET=my-secret
dvx plugin sync --project ./src/MyPlugin/MyPlugin.csproj
```

---

## Decorating plugins with attributes

Add the `beyro.PluginAttributes` NuGet package to your plugin project and apply
`[PluginStep]` to each plugin class.

### Basic step

```csharp
[PluginStep("account", "Create", Stage.PostOperation)]
public class AccountOnPostCreate : IPlugin { ... }
```

### All attribute properties

```csharp
[PluginStep(
    entity:  "account",
    message: "Update",
    stage:   Stage.PreOperation,

    // Optional ↓
    ExecutionOrder      = 1,        // rank / execution order within the stage. Default: 1
    Async               = false,    // true = async (background) step. Default: false (synchronous)
    Description         = "...",    // description stored on the step record
    RunAsSystem         = false,    // true = run as the Dataverse system user. Default: false

    // Filtering attributes — step only fires when one of these fields changes (Update only)
    FilteringAttributes = new[] { "name", "statuscode", "telephone1" },

    // Unsecure configuration string passed to the plugin constructor
    Configuration       = "unsecure-config",

    // Pre-image — snapshot of the record BEFORE the operation
    UsePreImage         = true,
    PreImageAttributes  = new[] { "name", "address1_city" },  // empty = all attributes
    PreImageAlias       = "PreImage",        // alias to read it by in code; default "PreImage"

    // Post-image — snapshot of the record AFTER the operation (PostOperation only)
    UsePostImage        = true,
    PostImageAttributes = new[] { "name" },
    PostImageAlias      = "PostImage"        // alias to read it by in code; default "PostImage"
)]
public class AccountOnPreUpdate : IPlugin { ... }
```

### Stage enum values

| Value | Integer | When it fires |
|---|---|---|
| `Stage.PreValidation` | 10 | Before the core operation, outside the database transaction |
| `Stage.PreOperation` | 20 | Before the core operation, inside the database transaction |
| `Stage.PostOperation` | 40 | After the core operation, inside the database transaction |

### Multiple steps on one class

Apply `[PluginStep]` more than once to register the same class for multiple messages or entities:

```csharp
[PluginStep("account", "Create", Stage.PostOperation)]
[PluginStep("account", "Update", Stage.PostOperation, FilteringAttributes = new[] { "name" })]
public class AccountOnCreateOrUpdate : IPlugin { ... }
```

Each attribute instance creates one independent `sdkmessageprocessingstep` record.

### Accessing images in plugin code

When images are registered, access them in the plugin via the execution context.  
dvx always uses the aliases **`PreImage`** and **`PostImage`**:

```csharp
var preImage  = context.PreEntityImages["PreImage"];    // UsePreImage = true
var postImage = context.PostEntityImages["PostImage"];  // UsePostImage = true
```

---

## Commands

> Commands are grouped by artifact: `dvx plugin …` for plugin assemblies, `dvx webresource …` for
> web resources, and `dvx config …` for configuration. Connection options
> (`--env` / `--url` / `--client-id` / `--client-secret`), `--config`, `--dry-run`,
> and `--verbose` are shared across all commands.

### plugin sync

> Build, deploy, and register steps in a single operation. **This is the plugin command you'll use most.**

```
dvx plugin sync --project <path> [options]
```

| Option | Required | Default | Description |
|---|---|---|---|
| `--project` | ✓ | | Path to the plugin `.csproj` file |
| `--publisher-prefix` | ✓ | from config | Dataverse publisher prefix (e.g. `solu`). Falls back to `publisherPrefix` in config |
| `--env` | | from config | Environment name from config. Not needed when `defaultEnvironment` is set or when connection options are provided directly |
| `--url` | | env var / config | Dataverse environment URL |
| `--client-id` | | env var / config | Service principal client ID |
| `--client-secret` | | env var / config | Service principal client secret |
| `--solution-unique-name` | | from config | Add all registered steps to this Dataverse solution |
| `--dry-run` | | | Print what would change without writing to Dataverse |
| `--config` | | auto-discovered | Path to config file |
| `--verbose` | | | Log upload details + inner exception details on error |

**What it does:**

1. Runs `dotnet build` on the `.csproj` to produce a `.nupkg` and `.dll`
2. Looks up the existing `pluginpackage` record by `uniquename` (`{prefix}_{assemblyName}`)
3. Uploads the new `.nupkg` by updating the `pluginpackage` `content` column via the Dataverse SDK
4. Queries the child `pluginassembly` record for the assembly ID
5. Reflects the `.dll` for `[PluginStep]` attributes
6. Fully syncs `sdkmessageprocessingstep` records — creates new steps, updates changed steps, deletes orphan steps
7. Syncs `sdkmessageprocessingstepimage` records (pre/post images) for each step

> **Note:** `sync` and `deploy` only support **updating** an existing plugin package.
> For the very first upload, register the package once with the Plugin Registration Tool. 
> After that, dvx handles all subsequent updates itself using the Dataverse SDK.

**Examples:**

```bash
# Developer local — defaultEnvironment in dvx.json, secret from env var
dvx plugin sync --project ./src/MyPlugin/MyPlugin.csproj

# Explicit environment
dvx plugin sync --env uat --project ./src/MyPlugin/MyPlugin.csproj

# CI/CD pipeline — no config file
dvx plugin sync \
  --url https://your-org.crm4.dynamics.com \
  --client-id  $CLIENT_ID \
  --client-secret $CLIENT_SECRET \
  --publisher-prefix yourprefix \
  --project ./src/MyPlugin/MyPlugin.csproj

# Dry-run (reads Dataverse but writes nothing)
dvx plugin sync --project ./src/MyPlugin/MyPlugin.csproj --dry-run
```

---

### plugin deploy

> Build the project and push the plugin package to Dataverse. Does not touch step registrations.

```
dvx plugin deploy --project <path> [options]
```

| Option | Required | Default | Description |
|---|---|---|---|
| `--project` | ✓ | | Path to the plugin `.csproj` file |
| `--publisher-prefix` | ✓ | from config | Dataverse publisher prefix. Falls back to `publisherPrefix` in config |
| `--env` | | from config | Environment name from config |
| `--url` | | env var / config | Dataverse environment URL |
| `--client-id` | | env var / config | Service principal client ID |
| `--client-secret` | | env var / config | Service principal client secret |
| `--config` | | auto-discovered | Path to config file |
| `--verbose` | | | Log upload details + inner exception details on error |

Use `deploy` when you want to push a new package version without changing step registrations,
or when step registrations are managed separately.

**Example:**

```
dvx plugin deploy --env uat --project ./src/MyPlugin/MyPlugin.csproj
```

---

### plugin register

> Reflect an already-deployed assembly for `[PluginStep]` attributes and sync step registrations.
> Does **not** build or re-upload the assembly.

```
dvx plugin register (--project <path> | --assembly-name <name>) [options]
```

| Option | Required | Description |
|---|---|---|
| `--project` | one of | Build the project, extract the DLL locally, use that for reflection |
| `--assembly-name` | one of | Download the DLL bytes from `pluginassembly.content` in Dataverse for reflection |
| `--env` | | Environment name from config |
| `--url` | | Dataverse environment URL |
| `--client-id` | | Service principal client ID |
| `--client-secret` | | Service principal client secret |
| `--solution-unique-name` | | Add all registered steps to this Dataverse solution. Falls back to `solutionUniqueName` in config |
| `--dry-run` | | Print what would change without writing to Dataverse |
| `--verbose` | | Log solution validation and step-assignment details |
| `--config` | | Path to config file |

`--project` and `--assembly-name` are mutually exclusive.

**Using `--project`** (recommended — reflects the exact DLL you last built):

```
dvx plugin register --env dev --project ./src/MyPlugin/MyPlugin.csproj
```

**Using `--assembly-name`** (reflects the DLL currently stored in Dataverse):

```
dvx plugin register --env dev --assembly-name MyPlugin
```

> **Note:** `--assembly-name` only works when the assembly was deployed with `sourcetype = Database`
> (the default for all dvx plugin deployments). If the assembly has no content bytes stored,
> use `--project` instead.

---

### plugin adopt

> **Onboard an existing project.** Read the steps already registered on an assembly in Dataverse and
> write matching `[PluginStep]` attributes into your source. A one-time bootstrap — afterwards use
> `sync` / `register`.

```
dvx plugin adopt --project <path> [options]
```

| Option | Required | Description |
|---|---|---|
| `--project` | ✓ | Path to the plugin `.csproj`. Its source files are edited in place. Falls back to config, then a single `.csproj` in the CWD |
| `--assembly-name` | | Dataverse `pluginassembly` name. Defaults to the project's assembly name |
| `--env` | | Environment name from config |
| `--url` / `--client-id` / `--client-secret` | | Connection values, resolved like every other command |
| `--config` | | Path to config file |
| `--dry-run` | | Print the attributes that would be written without modifying any files |
| `--verbose` | | Log per-file and per-step details |

**What it does:**

1. Resolves the `pluginassembly` in Dataverse by name.
2. Reads every step on that assembly's plugin types, plus their images and unsecure configuration.
3. Matches each step to its class by fully-qualified type name and inserts a `[PluginStep(...)]`
   attribute (adding `using beyro.PluginAttributes;` where needed).
4. Skips classes that already carry an equivalent attribute, and reports any Dataverse step whose
   class it could not find in the project.

> `adopt` never writes to Dataverse — it only edits source files. Review the result with `git diff`,
> then run `dvx plugin sync` to bring Dataverse under attribute control.

---

### webresource sync

> Upsert and publish Dataverse web resources from a folder and/or a manifest. **Upsert-only by
> default** — nothing is deleted unless you pass `--delete-orphaned`. Alias: `dvx wr sync`.

```
dvx webresource sync [options]
```

| Option | Required | Default | Description |
|---|---|---|---|
| `--folder` | one of folder/manifest | from config | Folder to auto-upsert from, recursively. Each file's name is derived as `{prefix}_/{relativePath}`. Falls back to `webResources.folder` |
| `--manifest` | one of folder/manifest | from config | Manifest JSON of explicit `{ dataverseName, localPath, displayName, type }` entries. Falls back to `webResources.manifest` |
| `--publisher-prefix` | folder mode (unless a solution is set) | from config | Publisher customization prefix for folder-derived names. Falls back to `publisherPrefix` in config. **Ignored when a solution is provided** — the solution's publisher prefix wins |
| `--solution-unique-name` | for `--delete-orphaned` | from config | Add upserted resources to this solution; the scope for orphan deletion; and, when set, the **source of the publisher prefix** for folder-derived names |
| `--delete-orphaned` | | off | Delete web resources in the solution that are no longer in source. **Requires a solution.** Destructive — run with `--dry-run` first |
| `--no-publish` | | publish on | Skip the publish step after upsert |
| `--env` / `--url` / `--client-id` / `--client-secret` | | env var / config | Connection values, resolved like every other command |
| `--dry-run` | | | Print what would change without writing to Dataverse |
| `--config` | | auto-discovered | Path to config file |
| `--verbose` | | | Per-resource tracing: resolved inputs, scan results, query hit/miss, diff outcome, solution add, orphan deletes, publish set |

**What it does:**

1. Builds the desired set from the folder (recursive scan) and/or the manifest.
2. For each resource: reads the local file, queries Dataverse by `name`, and **content-diffs**
   (line-ending-normalized for text types; byte-for-byte for binary). Unchanged → **skipped**;
   changed → **updated**; missing → **created**.
3. When a solution is set, adds each created/updated resource to it.
4. With `--delete-orphaned`, deletes resources that are in the solution but not in source.
5. Publishes all created/updated resources in one `PublishXml` call (unless `--no-publish`).

See [Web resources](#web-resources) for folder layout, naming, and type inference.

**Examples:**

```bash
# Folder + everything from dvx.json (webResources.folder, solutionUniqueName / publisherPrefix)
dvx webresource sync --env dev

# Explicit folder, preview only
dvx webresource sync --env dev --folder ./WebResources --publisher-prefix pub --dry-run --verbose

# Manifest of explicit mappings
dvx webresource sync --env dev --manifest ./webresources.json

# Prune resources removed from source (scoped to the solution)
dvx webresource sync --env dev --solution-unique-name MySolution --delete-orphaned
```

---

### config create

> Write a template `dvx.json` to the current directory (or `--location`).

```
dvx config create [--location <dir>] [--overwrite]
```

| Option | Default | Description |
|---|---|---|
| `--location` | current dir | Directory to create `dvx.json` in |
| `--overwrite` | | Overwrite `dvx.json` if it already exists |

---

## Web resources

`dvx webresource sync` deploys web resources (JavaScript, CSS, HTML, images, …) from your repo to
Dataverse. It content-diffs every resource and only writes what changed, so re-running it is cheap
and idempotent. The source can be a **folder** (auto-discovered), a **manifest** (explicit
mappings), or both.

### Folder mode

Point dvx at a folder and it upserts every file with a recognized extension, recursively. The
Dataverse `name` of each resource is derived from its path (forward slashes):

```
name = {prefix}_/{relativePath}
```

With `pub` as the prefix (see below for where it comes from):

```
WebResources/
  account/main.js   ->  pub_/account/main.js
  shared/util.css   ->  pub_/shared/util.css
```

The prefix is always the publisher's customization prefix. When a solution is provided
(`--solution-unique-name` or `solutionUniqueName`), dvx reads the prefix from that solution's
publisher; otherwise it uses `--publisher-prefix` / `publisherPrefix`. If both are supplied the
solution wins and dvx warns that the configured prefix is ignored.

Files with unrecognized extensions are skipped (and listed under `--verbose`). Dotfiles,
`*.map` source maps, and `node_modules` / `bin` / `obj` / `.git` directories are ignored.

```bash
dvx webresource sync --env dev --folder ./WebResources --name-prefix pub
```

### Manifest mode

For explicit control (custom names, display names, non-derivable types), pass a manifest JSON — an
array of entries matching the legacy `Sync-WebResources.ps1` shape:

```json
[
  {
    "dataverseName": "pub_/account/main.js",
    "localPath": "./WebResources/account/main.js",
    "displayName": "Account main script",
    "type": 3
  },
  {
    "dataverseName": "pub_/shared/logo.png",
    "localPath": "./WebResources/shared/logo.png"
  }
]
```

| Field | Required | Description |
|---|---|---|
| `dataverseName` | ✓ | The web resource `name` in Dataverse |
| `localPath` | ✓ | File to read content from. Relative paths resolve against the manifest file's directory |
| `displayName` | | Display name. Defaults to `dataverseName` |
| `type` | | `webresourcetype` value. Inferred from the file extension when omitted |

```bash
dvx webresource sync --env dev --manifest ./webresources.json
```

If both a folder and a manifest resolve, the union is processed; a manifest entry overrides a
folder-derived one with the same name.

### Web resource types

The type is inferred from the file extension (override per-entry with `type` in a manifest):

| Extension | Type | | Extension | Type |
|---|---|---|---|---|
| `.htm` / `.html` | 1 | | `.gif` | 7 |
| `.css` | 2 | | `.xap` | 8 |
| `.js` | 3 | | `.xsl` / `.xslt` | 9 |
| `.xml` | 4 | | `.ico` | 10 |
| `.png` | 5 | | `.svg` | 11 |
| `.jpg` / `.jpeg` | 6 | | `.resx` | 12 |

Text types (HTML, CSS, JS, XML, XSL, SVG, RESX) are compared with line endings normalized, so a
pure CRLF↔LF difference counts as no change. Other types are compared byte-for-byte.

### Publishing

After upserting, dvx publishes all created/updated resources in a single `PublishXml` request, so
the changes go live without a manual publish. Pass `--no-publish` (or `"publish": false` in config)
to skip it.

### Solution membership

When a solution is set (`--solution-unique-name` or `solutionUniqueName` in config), each
created/updated resource is added to that solution (idempotent).

### Deleting orphans

By default nothing is deleted. Pass `--delete-orphaned` to remove web resources that are **in the
target solution** but no longer present in your folder/manifest. Because the deletion scope is the
solution, `--delete-orphaned` **requires a solution**. Always preview with `--dry-run` first:

```bash
dvx webresource sync --env dev --solution-unique-name MySolution --delete-orphaned --dry-run
dvx webresource sync --env dev --solution-unique-name MySolution --delete-orphaned
```

### Configuration

Set defaults under `webResources` in `dvx.json` so the command needs no extra arguments:

```json
{
  "defaultEnvironment": "dev",
  "publisherPrefix": "pub",
  "solutionUniqueName": "MySolution",
  "environments": [ /* … */ ],
  "webResources": {
    "folder": "./WebResources",
    "publish": true
  }
}
```

```bash
# Everything resolved from config:
dvx webresource sync --env dev
```

| Field | Description |
|---|---|
| `folder` | Default folder to upsert from |
| `manifest` | Default manifest path |
| `publish` | Publish after upsert. Default `true` |

---

## Adopting an existing project

Already have plugins registered in Dataverse but no `[PluginStep]` attributes in code? `adopt`
scaffolds them for you.

**1. Reference the attributes package** and supply connection details (see [Quick start](#quick-start)).

**2. Preview** the attributes that would be written:

```
dvx plugin adopt --env dev --project ./src/MyPlugin/MyPlugin.csproj --dry-run
```

**3. Write them** and review the diff:

```
dvx plugin adopt --env dev --project ./src/MyPlugin/MyPlugin.csproj
git diff
```

**4. Sync.** The first sync adopts the existing steps in place (matching by entity + message +
stage + sync/async), so there is no churn — expect updates only:

```
dvx plugin sync --env dev --project ./src/MyPlugin/MyPlugin.csproj --dry-run
dvx plugin sync --env dev --project ./src/MyPlugin/MyPlugin.csproj
```

**Things to review after adoption:**
- Steps whose **class can't be found** in the project are reported and skipped — manage those by hand.
- `supporteddeployment` other than *Server Only* is not represented by `[PluginStep]`.

---

## How step registration works

### Step naming

dvx writes every step with a name in the format:

```
Namespace.ClassName | entity | message | StageName | sync|async
```

For example:
```
MyPlugin.AccountOnPostCreate | account | create | PostOperation | sync
```

dvx reconciles steps **per plugin assembly**: it only looks at steps registered on the
plugin types contained in the assembly it just deployed. Steps belonging to other assemblies are
never touched. Within the assembly, each `[PluginStep]` is matched to an existing step first by
this exact name, then — for steps you registered by hand or with another tool — by **identity**
(entity + message + stage + sync/async). A match found by identity is **adopted in place**
(updated and renamed to the convention above) rather than deleted and recreated. See
[Adopting an existing project](#adopting-an-existing-project).

### Full sync behaviour

Each time `register` or `sync` runs, dvx performs a full sync for the target assembly:

| Scenario | What happens |
|---|---|
| `[PluginStep]` attribute exists, no matching step in Dataverse | Step **created** |
| `[PluginStep]` attribute exists, matching step (by name) in Dataverse | Step **updated** to match attribute values |
| `[PluginStep]` attribute exists, hand-registered step with matching identity | Step **adopted** — updated and renamed in place |
| Step on one of this assembly's plugin types with no matching attribute | Step **deleted** (orphan) |
| `[PluginStep]` attribute removed from code | Step **deleted** on next sync |
| Plugin class deleted from code | Step **deleted** on next sync |
| Step on a plugin type in a *different* assembly | **Untouched** |

> **Adoption note:** because any step on this assembly's plugin types with no matching attribute is
> treated as an orphan and deleted, decorate **all** existing steps before your first `sync`. The
> [`plugin adopt`](#plugin-adopt) command does this for you.

### IPlugin class with no `[PluginStep]` attribute

If dvx finds a class that implements `IPlugin` but has no `[PluginStep]` attribute, it
logs a **warning** and skips that class. All other steps are still processed.

### Solution membership

When `--solution-unique-name` (or `solutionUniqueName` in config) is set, dvx adds each
created or updated step to that solution after writing it to Dataverse. Steps that already belong
to the solution are unaffected (the operation is idempotent). If the solution does not exist,
dvx exits with an error **before** registering any steps.

### `sdkmessagefilter` validation

Before creating an **entity-specific** step, dvx checks that Dataverse has an
`sdkmessagefilter` record for the given entity + message combination. If none exists (e.g. you
specify an entity that doesn't support that message), the step is **skipped with a warning**
rather than causing an error.

### Entity-less (global) messages

Some messages — such as `Associate` and `Disassociate` — are not tied to a specific entity and
have no `sdkmessagefilter`. Omit `Entity` on `[PluginStep]` for these, and dvx plugin registers the
step with no filter (so it fires for the message regardless of entity):

```csharp
[PluginStep("", "Associate", Stage.PostOperation)]
public class AssociationPlugin : IPlugin { ... }
```

---

## Pre- and post-images

Enable images by setting `UsePreImage = true` and/or `UsePostImage = true` on `[PluginStep]`.

```csharp
[PluginStep("contact", "Update", Stage.PostOperation,
    FilteringAttributes = new[] { "firstname", "lastname" },
    UsePreImage         = true,
    PreImageAttributes  = new[] { "firstname", "lastname", "emailaddress1" },
    UsePostImage        = true)]
public class ContactOnPostUpdate : IPlugin { ... }
```

| Property | Default | Description |
|---|---|---|
| `UsePreImage` | `false` | Register a pre-image snapshot on this step |
| `PreImageAttributes` | `[]` (all) | Fields to include in the pre-image. Empty = include all |
| `PreImageAlias` | `"PreImage"` | Entity alias to read the pre-image by |
| `UsePostImage` | `false` | Register a post-image snapshot on this step |
| `PostImageAttributes` | `[]` (all) | Fields to include in the post-image. Empty = include all |
| `PostImageAlias` | `"PostImage"` | Entity alias to read the post-image by |

**Constraints:**
- Post-images are only valid on `Stage.PostOperation`. If `UsePostImage = true` on any other
  stage, dvx logs a warning and skips the image.
- The alias defaults to `"PreImage"` / `"PostImage"`, but can be overridden with
  `PreImageAlias` / `PostImageAlias`. Reference the image by whichever alias you set:
  ```csharp
  context.PreEntityImages["PreImage"]    // or your custom PreImageAlias
  context.PostEntityImages["PostImage"]  // or your custom PostImageAlias
  ```

---

## Unsecure configuration

Plugins can receive an unsecure configuration string at runtime via their constructor. Set it on
`[PluginStep]`:

```csharp
[PluginStep("account", "Create", Stage.PostOperation,
    Configuration = "<settings>...</settings>")]
public class ConfiguredPlugin : IPlugin { ... }
```

| Property | Maps to | Description |
|---|---|---|
| `Configuration` | `sdkmessageprocessingstep.configuration` | Unsecure config — readable by anyone who can view the step |

It maps to the plugin constructor's first parameter:

```csharp
public ConfiguredPlugin(string unsecureConfiguration, string secureConfiguration) { ... }
```

**Notes:**
- Optional. When omitted, `configuration` is cleared on the step.
- dvx does **not** manage **secure** configuration (`sdkmessageprocessingstepsecureconfig`). Secure
  config is environment-specific and not solution-aware, so manage it out of band (e.g. with the
  Plugin Registration Tool) per environment.

---

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success — all operations completed |
| `1` | Fatal error — config missing, auth failed, assembly not found, etc. |
| `2` | Partial failure — one or more steps failed but others succeeded |

---

## Dataverse tables used

dvx reads and writes the following Dataverse tables:

| Table (logical name) | Purpose |
|---|---|
| `pluginpackage` | Stores the plugin package (nupkg) in its `content` column. Queried by `uniquename`, then updated with the new `.nupkg` content on deploy. |
| `pluginassembly` | Child record created by Dataverse when it processes a plugin package. Queried after deploy to get the ID for step registration. Also queried by `--assembly-name` to download content bytes. |
| `plugintype` | One record per plugin class. Queried to resolve class names to GUIDs for step registration. |
| `sdkmessage` | Lookup table for message names (`Create`, `Update`, `Delete`, …). Loaded once and cached per run. |
| `sdkmessagefilter` | Associates messages with entity types and indicates whether custom steps are allowed. |
| `sdkmessageprocessingstep` | The step registration itself. Created, updated, and deleted by dvx. |
| `sdkmessageprocessingstepimage` | Pre- and post-image registrations attached to a step. |
| `webresource` | Web resource records. Queried by `name`, created/updated/deleted, and published by `webresource sync`. |
| `solutioncomponent` | Queried (joined to `webresource`) to find web resources in a solution for `--delete-orphaned`. |
| `solution` | Queried by unique name to validate the target solution exists, and to add steps / web resources to it. |

---

## Project structure

```
PluginRegistrationTool/
├── src/
│   ├── beyro.PluginAttributes/        # netstandard2.0 NuGet package
│   │   ├── PluginStepAttribute.cs         # [PluginStep] attribute with all config
│   │   ├── Stage.cs                       # PreValidation / PreOperation / PostOperation
│   │   └── beyro.PluginAttributes.csproj
│   ├── dvx/                          # net8 CLI tool
│   │   ├── Commands/
│   │   │   ├── DeployCommand.cs           # dvx plugin deploy
│   │   │   ├── RegisterCommand.cs         # dvx plugin register
│   │   │   ├── SyncCommand.cs             # dvx plugin sync
│   │   │   ├── AdoptCommand.cs            # dvx plugin adopt
│   │   │   ├── WebResourceSyncCommand.cs  # dvx webresource sync
│   │   │   ├── CreateConfigCommand.cs     # dvx config create
│   │   │   └── Shared/CommandOptions.cs   # Shared option definitions
│   │   ├── Config/
│   │   │   └── ConfigLoader.cs            # Config discovery, env resolution, prefix resolution
│   │   ├── Models/
│   │   │   ├── AppConfig.cs               # Root config model (+ WebResourceConfig)
│   │   │   ├── EnvironmentConfig.cs       # Per-environment connection details
│   │   │   ├── WebResourceConfig.cs       # webResources config section
│   │   │   ├── WebResourceDefinition.cs   # Resolved web resource to upsert
│   │   │   ├── WebResourceManifestEntry.cs# One manifest JSON entry
│   │   │   ├── PluginStepDefinition.cs    # Resolved step (from reflection or Dataverse)
│   │   │   ├── ImageDefinition.cs         # Pre/post image definition
│   │   │   ├── SyncResult.cs              # Created / updated / deleted / skipped / published counters
│   │   │   ├── ImportResult.cs            # adopt: imported definitions + warnings
│   │   │   └── AttributeWriteResult.cs    # adopt: attributes added / skipped / unmatched
│   │   ├── Output/
│   │   │   └── Out.cs                     # Console output helper
│   │   ├── Services/
│   │   │   ├── DataverseClientFactory.cs  # Constructs ServiceClient from EnvironmentConfig
│   │   │   ├── ProjectBuilder.cs          # Runs dotnet build → BuildResult(NupkgPath, DllPath)
│   │   │   ├── PackageDeployer.cs         # Looks up pluginpackage ID, uploads .nupkg via SDK content update
│   │   │   ├── AssemblyDownloader.cs      # Downloads DLL bytes from Dataverse content field
│   │   │   ├── PluginDiscovery.cs         # MetadataLoadContext reflection → step definitions
│   │   │   ├── SdkMetadata.cs             # Shared message/filter/plugintype lookups (both directions)
│   │   │   ├── StepImporter.cs            # adopt: Dataverse steps → step definitions (reverse of StepRegistrar)
│   │   │   ├── AttributeWriter.cs         # adopt: writes [PluginStep] attributes into source via Roslyn
│   │   │   ├── SolutionService.cs         # Solution add (steps/web resources); solution web-resource query
│   │   │   ├── StepRegistrar.cs           # Full sync: upsert steps + adopt/delete orphans + images
│   │   │   ├── WebResourceTypes.cs        # Extension → webresourcetype + text/binary classification
│   │   │   ├── WebResourceFolderScanner.cs# Folder walk → web resource definitions ({prefix}_/path)
│   │   │   └── WebResourceSyncer.cs       # Upsert + content-diff + solution add + orphan delete + publish
│   │   ├── Program.cs
│   │   └── dvx.csproj
│   └── dvx.Tests/                    # xUnit test project
└── dvx.sln
```
