using System.Text;
using dvx.Models;
using dvx.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Upserts web resources into Dataverse from a set of <see cref="WebResourceDefinition"/>s:
    /// content-diffs each one (line-ending-normalized for text, byte-for-byte for binary),
    /// creates or updates only what changed, optionally adds each to a solution, optionally deletes
    /// solution members that are no longer present in the desired set, and finally publishes the
    /// changed resources. Mirrors the upsert flow of <see cref="StepRegistrar"/>.
    /// </summary>
    public class WebResourceSyncer(
        IOrganizationService svc,
        ILogger<WebResourceSyncer> logger,
        SolutionService? solutionService = null)
    {
        private IOrganizationService _svc = svc;
        private readonly SolutionService _solutionService = solutionService ?? new SolutionService(svc);

        public SyncResult Sync(
            IReadOnlyList<WebResourceDefinition> desired,
            bool    dryRun             = false,
            string? solutionUniqueName = null,
            bool    publish            = true,
            bool    deleteOrphaned     = false,
            bool    verbose            = false)
        {
            var result = new SyncResult();

            // An "orphan" is a resource in the target solution but not in the desired set — so the
            // deletion scope is the solution. Without one, the flag is meaningless and dangerous.
            if (deleteOrphaned && solutionUniqueName is null)
                throw new InvalidOperationException(
                    "--delete-orphaned requires a solution. Set --solution-unique-name or " +
                    "solutionUniqueName in config.");

            if (solutionUniqueName is not null)
                _solutionService.ValidateSolutionExists(solutionUniqueName, verbose);

            var originalSvc = _svc;
            if (dryRun)
                _svc = new ReadOnlyOrganizationService(_svc);

            try
            {
                var managedIds = new HashSet<Guid>();           // desired resources that exist in Dataverse
                var publishIds = new List<Guid>();              // created/updated → need publishing

                // Dedupe by name; the later definition wins, so a manifest entry (appended after the
                // folder scan by the command) overrides a folder-derived one with the same name.
                var deduped = new Dictionary<string, WebResourceDefinition>(StringComparer.OrdinalIgnoreCase);
                foreach (var def in desired)
                    deduped[def.Name] = def;

                foreach (var def in deduped.Values)
                {
                    try
                    {
                        ProcessOne(def, solutionUniqueName, dryRun, verbose, result, managedIds, publishIds);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to sync web resource '{def.Name}': {ex.Message}");
                    }
                }

                if (deleteOrphaned)
                    DeleteOrphans(solutionUniqueName!, managedIds, dryRun, verbose, result);

                if (publish && publishIds.Count > 0)
                {
                    if (dryRun)
                    {
                        if (verbose) Out.Dim($"    Would publish {publishIds.Count} web resource(s).");
                    }
                    else
                    {
                        PublishWebResources(publishIds, verbose);
                        result.Published = publishIds.Count;
                    }
                }

                return result;
            }
            finally
            {
                _svc = originalSvc;
            }
        }

        // ── Per-resource upsert ────────────────────────────────────────────────

        private void ProcessOne(
            WebResourceDefinition def, string? solutionUniqueName, bool dryRun, bool verbose,
            SyncResult result, HashSet<Guid> managedIds, List<Guid> publishIds)
        {
            if (!File.Exists(def.LocalPath))
            {
                result.Warnings.Add($"Local file not found for '{def.Name}': {def.LocalPath} — skipping.");
                return;
            }

            int type;
            if (def.Type.HasValue)
                type = def.Type.Value;
            else if (!WebResourceTypes.TryInferType(def.LocalPath, out type))
            {
                result.Warnings.Add(
                    $"Cannot infer web resource type for '{def.Name}' " +
                    $"({Path.GetFileName(def.LocalPath)}) — specify 'type' in the manifest. Skipping.");
                return;
            }

            var isText = WebResourceTypes.IsText(type);
            var (localBase64, localCompare) = ReadLocal(def.LocalPath, isText);

            if (verbose) Out.Dim($"    Querying webresource '{def.Name}'...");
            var existing = FindWebResource(def.Name);

            if (existing is { } e)
            {
                managedIds.Add(e.Id);

                var remoteBase64  = e.GetAttributeValue<string>("content") ?? string.Empty;
                var remoteCompare = DecodeForCompare(remoteBase64, isText);

                if (string.Equals(localCompare, remoteCompare, StringComparison.Ordinal))
                {
                    if (verbose) Out.Dim($"    '{def.Name}' unchanged — skipping.");
                    result.Skipped++;
                    return;
                }

                if (verbose) Out.Dim($"    '{def.Name}' content differs — updating ({e.Id}).");
                logger.LogInformation("Updating web resource: {Name}", def.Name);

                var update = new Entity("webresource", e.Id) { ["content"] = localBase64 };
                if (!string.IsNullOrWhiteSpace(def.DisplayName))
                    update["displayname"] = def.DisplayName;
                _svc.Update(update);

                publishIds.Add(e.Id);
                result.Updated++;

                if (solutionUniqueName is not null && !dryRun)
                    _solutionService.AddWebResourceToSolution(e.Id, solutionUniqueName, verbose);
            }
            else
            {
                if (verbose) Out.Dim($"    '{def.Name}' not found — creating (type {type}).");
                logger.LogInformation("Creating web resource: {Name}", def.Name);

                var create = new Entity("webresource")
                {
                    ["name"]            = def.Name,
                    ["displayname"]     = string.IsNullOrWhiteSpace(def.DisplayName) ? def.Name : def.DisplayName,
                    ["webresourcetype"] = new OptionSetValue(type),
                    ["content"]         = localBase64,
                };
                var id = _svc.Create(create);

                managedIds.Add(id);
                publishIds.Add(id);
                result.Created++;

                if (solutionUniqueName is not null && !dryRun)
                    _solutionService.AddWebResourceToSolution(id, solutionUniqueName, verbose);
            }
        }

        // ── Orphan deletion ────────────────────────────────────────────────────

        private void DeleteOrphans(
            string solutionUniqueName, HashSet<Guid> managedIds, bool dryRun, bool verbose, SyncResult result)
        {
            var inSolution = _solutionService.GetSolutionWebResources(solutionUniqueName, verbose);
            var orphans    = inSolution.Where(w => !managedIds.Contains(w.Id)).ToList();

            if (verbose)
                Out.Dim($"    Orphan check: {inSolution.Count} in solution, " +
                        $"{managedIds.Count} managed, {orphans.Count} orphan(s).");

            foreach (var (id, name) in orphans)
            {
                if (verbose) Out.Dim($"    Deleting orphan '{name}' ({id}).");
                logger.LogInformation("Deleting orphan web resource: {Name}", name);
                _svc.Delete("webresource", id);   // no-op in dry-run via ReadOnlyOrganizationService
                result.Deleted++;
            }
        }

        // ── Publish ────────────────────────────────────────────────────────────

        private void PublishWebResources(IReadOnlyList<Guid> ids, bool verbose)
        {
            var sb = new StringBuilder("<importexportxml><webresources>");
            foreach (var id in ids)
                sb.Append("<webresource>").Append(id).Append("</webresource>");
            sb.Append("</webresources></importexportxml>");

            if (verbose) Out.Dim($"    Publishing {ids.Count} web resource(s)...");
            _svc.Execute(new OrganizationRequest("PublishXml") { ["ParameterXml"] = sb.ToString() });
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private Entity? FindWebResource(string name)
        {
            var query = new QueryExpression("webresource")
            {
                ColumnSet = new ColumnSet("webresourceid", "content", "webresourcetype"),
                Criteria  = new FilterExpression(),
                TopCount  = 1,
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);

            var result = _svc.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        /// <summary>
        /// Reads the local file and returns the base64 content to store plus a comparison key.
        /// Text files store their original bytes but compare with normalized line endings (so an
        /// LF/CRLF-only difference is treated as no change); binary files compare by base64 bytes.
        /// </summary>
        private static (string Base64, string CompareKey) ReadLocal(string path, bool isText)
        {
            if (isText)
            {
                var text   = File.ReadAllText(path);
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                return (base64, NormalizeLineEndings(text));
            }

            var b64 = Convert.ToBase64String(File.ReadAllBytes(path));
            return (b64, b64);
        }

        private static string DecodeForCompare(string remoteBase64, bool isText)
        {
            if (string.IsNullOrEmpty(remoteBase64))
                return string.Empty;

            if (!isText)
                return remoteBase64;

            try
            {
                var bytes = Convert.FromBase64String(remoteBase64);
                return NormalizeLineEndings(Encoding.UTF8.GetString(bytes));
            }
            catch (FormatException)
            {
                return remoteBase64;
            }
        }

        private static string NormalizeLineEndings(string s)
            => s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
    }
}
