using dvx.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    public class StepRegistrar(
        IOrganizationService svc,
        ILogger<StepRegistrar> logger,
        SolutionService? solutionService = null)
    {
        private IOrganizationService _svc = svc;
        private readonly SolutionService _solutionService = solutionService ?? new SolutionService(svc);
        private Guid _systemUserId;

        public SyncResult Sync(Guid assemblyId, IReadOnlyList<PluginStepDefinition> definitions,
                               bool dryRun = false, string? solutionUniqueName = null,
                               bool deleteOrphaned = false, bool verbose = false)
        {
            var result = new SyncResult();
            var originalSvc = _svc;

            if (solutionUniqueName is not null)
                _solutionService.ValidateSolutionExists(solutionUniqueName, verbose);

            if (dryRun)
                _svc = new ReadOnlyOrganizationService(_svc);

            try
            {
                // ── Build lookup caches ────────────────────────────────────────────
                var meta = new SdkMetadata(_svc);
                _systemUserId = meta.SystemUserId();
                var msgCache = meta.MessageIdByName();
                var filterCache = meta.SdkFilterIdsByEntityNames(ReferencedEntityNames(definitions));
                var typeCache = meta.PluginTypeIdByName(assemblyId);

                if (typeCache.Count == 0)
                {
                    result.Errors.Add(
                        $"No plugintype records found for assembly {assemblyId}. " +
                        "Ensure the assembly is deployed before running register.");
                    return result;
                }

                // ── Existing steps on these plugin types ───────────────────────────
                var existingSteps = LoadExistingSteps(typeCache);
                var byName     = new Dictionary<string, ExistingStep>(StringComparer.Ordinal);
                var byIdentity = new Dictionary<StepIdentity, ExistingStep>();
                foreach (var s in existingSteps)
                {
                    byName[s.Name] = s;
                    byIdentity.TryAdd(s.Identity, s);
                }

                // ── Upsert loop ────────────────────────────────────────────────────
                var consumed = new HashSet<Guid>();

                foreach (var def in definitions)
                {
                    if (!msgCache.TryGetValue(def.Message, out var msgId))
                    {
                        result.Warnings.Add($"Unknown SDK message '{def.Message}' — skipping {def.TypeFullName}.");
                        continue;
                    }

                    // Entity-specific steps reference an sdkmessagefilter. Global messages such as
                    // Associate / Disassociate carry no entity and have no filter — they register
                    // with a null sdkmessagefilterid instead of being skipped.
                    Guid? filterId = null;
                    if (!string.IsNullOrWhiteSpace(def.Entity))
                    {
                        var filterKey = new MessageEntityKey(msgId, def.Entity);
                        if (!filterCache.TryGetValue(filterKey, out var entityFilterId))
                        {
                            result.Warnings.Add(
                                $"No sdkmessagefilter for entity '{def.Entity}' + message '{def.Message}' — skipping {def.TypeFullName}.");
                            continue;
                        }

                        if (!IsCustomStepAllowed(entityFilterId))
                        {
                            result.Warnings.Add(
                                $"Custom steps not allowed for entity '{def.Entity}' + message '{def.Message}' — skipping {def.TypeFullName}.");
                            continue;
                        }

                        filterId = entityFilterId;
                    }

                    if (!typeCache.TryGetValue(def.TypeFullName, out var pluginTypeId))
                    {
                        result.Errors.Add(
                            $"plugintype '{def.TypeFullName}' not found in Dataverse. Was the assembly deployed correctly?");
                        continue;
                    }

                    ValidateImages(def, result);

                    try
                    {
                        // Match an existing step by exact name first (steps dvx already owns), then by
                        // identity (steps registered by hand / other tools) so the first sync after adoption
                        // updates them in place instead of deleting and recreating.
                        // A global message (no entity filter) has filterId == null here, but existing
                        // steps load their missing filter as Guid.Empty — normalize so identities match.
                        var identity = new StepIdentity(pluginTypeId, msgId, filterId ?? Guid.Empty, def.Stage, def.Mode);
                        ExistingStep? match =
                            byName.TryGetValue(def.StepName, out var nameMatch) && !consumed.Contains(nameMatch.StepId)
                                ? nameMatch
                                : byIdentity.TryGetValue(identity, out var idMatch) && !consumed.Contains(idMatch.StepId)
                                    ? idMatch
                                    : null;

                        if (match is { } existing)
                        {
                            consumed.Add(existing.StepId);
                            // Skip steps whose desired state already matches Dataverse so the summary
                            // reflects real changes (a no-op re-sync reports Skipped, not Updated).
                            if (StepUnchanged(def, existing))
                                result.Skipped++;
                            else
                                UpdateStep(def, existing.StepId,
                                    msgId, filterId, pluginTypeId, result);
                            if (solutionUniqueName is not null && !dryRun)
                                _solutionService.AddStepToSolution(existing.StepId, solutionUniqueName, verbose);
                        }
                        else
                        {
                            var newStepId = CreateStep(def, msgId, filterId, pluginTypeId, result);
                            if (solutionUniqueName is not null && !dryRun)
                                _solutionService.AddStepToSolution(newStepId, solutionUniqueName, verbose);
                        }
                    }
                    catch (Exception ex)
                    {
                        // A Dataverse fault while registering this step (e.g. an invalid filtering
                        // attribute) would otherwise abort the entire run with a message that never
                        // says which plugin it came from. Attribute the failure to its plugin class —
                        // keeping the original Dataverse text — and carry on with the rest.
                        var entityPart = string.IsNullOrWhiteSpace(def.Entity)
                            ? string.Empty
                            : $" on entity '{def.Entity}'";
                        result.Errors.Add(
                            $"Failed to register plugin class '{def.TypeFullName}' for message " +
                            $"'{def.Message}'{entityPart} ({PluginStepDefinition.StageName(def.Stage)}): {ex.Message}");
                    }
                }

                // ── Orphan handling ─────────────────────────────────────────────────
                // Steps that back a Custom API or Custom Action live on this assembly's plugin types
                // but are owned by the API/action definition, not by dvx — never treat them as orphans.
                var protectedMessageIds = meta.CustomApiMessageIds();
                protectedMessageIds.UnionWith(meta.CustomActionMessageIds());

                var orphans = existingSteps
                    .Where(s => !consumed.Contains(s.StepId) && !protectedMessageIds.Contains(s.MessageId))
                    .ToList();

                foreach (var orphan in orphans)
                {
                    if (deleteOrphaned)
                    {
                        logger.LogInformation("Deleting orphan step: {Name}", orphan.Name);
                        _svc.Delete("sdkmessageprocessingstep", orphan.StepId);
                        result.Deleted++;
                    }
                    else
                    {
                        result.Warnings.Add(
                            $"Orphaned step '{orphan.Name}' exists in Dataverse but not in code. " +
                            "Re-run with --delete-orphaned to remove it.");
                    }
                }

                return result;
            }
            finally
            {
                _svc = originalSvc;
            }
        }

        // ── Existing-step loader ───────────────────────────────────────────────

        /// <summary>The natural identity of a step, used to adopt hand-registered steps in place.</summary>
        private readonly record struct StepIdentity(
            Guid PluginTypeId, Guid MessageId, Guid FilterId, int Stage, int Mode);

        private record ExistingStep(
            Guid StepId, string Name,
            Guid PluginTypeId, Guid MessageId, Guid FilterId, int Stage, int Mode,
            int Rank, string? Description, string? FilteringAttributes, string? Configuration,
            Guid? ImpersonatingUserId)
        {
            public StepIdentity Identity => new(PluginTypeId, MessageId, FilterId, Stage, Mode);
        }

        private List<ExistingStep> LoadExistingSteps(Dictionary<string, Guid> typeCache)
        {
            if (typeCache.Count == 0)
                return new List<ExistingStep>();

            var typeIds = typeCache.Values.Cast<object>().ToArray();

            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepid", "name",
                    "plugintypeid", "sdkmessageid",
                    "sdkmessagefilterid", "stage", "mode",
                    "rank", "description", "filteringattributes", "configuration", "impersonatinguserid"),
                Criteria = new FilterExpression()
            };
            query.Criteria.AddCondition("plugintypeid", ConditionOperator.In, typeIds);

            var list = new List<ExistingStep>();
            foreach (var e in _svc.RetrieveMultiple(query).Entities)
            {
                var name = e.GetAttributeValue<string>("name");
                if (name is null) continue;

                var typeRef   = e.GetAttributeValue<EntityReference>("plugintypeid");
                var msgRef    = e.GetAttributeValue<EntityReference>("sdkmessageid");
                var filterRef = e.GetAttributeValue<EntityReference>("sdkmessagefilterid");

                list.Add(new ExistingStep(
                    e.Id,
                    name,
                    typeRef?.Id   ?? Guid.Empty,
                    msgRef?.Id    ?? Guid.Empty,
                    filterRef?.Id ?? Guid.Empty,
                    e.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                    e.GetAttributeValue<OptionSetValue>("mode")?.Value  ?? 0,
                    e.GetAttributeValue<int>("rank"),
                    NullIfEmpty(e.GetAttributeValue<string>("description")),
                    NullIfEmpty(e.GetAttributeValue<string>("filteringattributes")),
                    NullIfEmpty(e.GetAttributeValue<string>("configuration")),
                    e.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id));
            }

            return list;
        }

        // ── Step create / update ───────────────────────────────────────────────

        private Guid CreateStep(PluginStepDefinition def,
            Guid msgId, Guid? filterId, Guid pluginTypeId, SyncResult result)
        {
            logger.LogInformation("Creating step: {Name}", def.StepName);

            var step = BuildStepEntity(def, msgId, filterId, pluginTypeId);
            var stepId = _svc.Create(step);

            SyncImages(stepId, def);
            result.Created++;
            return stepId;
        }

        private void UpdateStep(PluginStepDefinition def,
            Guid stepId, Guid msgId, Guid? filterId, Guid pluginTypeId, SyncResult result)
        {
            logger.LogDebug("Updating step: {Name}", def.StepName);

            var step = BuildStepEntity(def, msgId, filterId, pluginTypeId);
            step.Id = stepId;
            _svc.Update(step);
            SyncImages(stepId, def);
            result.Updated++;
        }

        private Entity BuildStepEntity(PluginStepDefinition def,
            Guid msgId, Guid? filterId, Guid pluginTypeId)
        {
            var step = new Entity("sdkmessageprocessingstep");
            step["name"] = def.StepName;
            step["plugintypeid"] = new EntityReference("plugintype", pluginTypeId);
            step["sdkmessageid"] = new EntityReference("sdkmessage", msgId);
            // Global messages (Associate / Disassociate) have no entity filter; leave the lookup
            // null so the step fires for the message regardless of entity. Setting it explicitly
            // also clears a stale filter on update.
            step["sdkmessagefilterid"] = filterId.HasValue
                ? new EntityReference("sdkmessagefilter", filterId.Value)
                : null;
            step["stage"] = new OptionSetValue(def.Stage);
            step["mode"] = new OptionSetValue(def.Mode);
            step["rank"] = def.ExecutionOrder;
            step["supporteddeployment"] = new OptionSetValue(0); // Server Only
            step["asyncautodelete"] = def.Mode == 1;

            // null  → calling user (impersonatinguserid = null)
            // Guid.Empty → SYSTEM user (impersonatinguserid = actual SYSTEM user guid)
            // other Guid → impersonate that specific user
            var impersonationId = def.RunAsUser == Guid.Empty ? _systemUserId : def.RunAsUser;
            step["impersonatinguserid"] = impersonationId.HasValue
                ? new EntityReference("systemuser", impersonationId.Value)
                : null;

            // Always assign so removing all filtering attributes clears a stale value on update,
            // mirroring the sdkmessagefilterid handling above (null clears the column).
            step["filteringattributes"] = def.FilteringAttributes.Length > 0
                ? string.Join(",", def.FilteringAttributes)
                : null;

            step["description"] = NullIfEmpty(def.Description);
            step["configuration"] = NullIfEmpty(def.Configuration);

            return step;
        }

        // ── Change detection ───────────────────────────────────────────────────

        /// <summary>
        /// True when the existing step already matches the desired definition, so no update is
        /// needed. Compares exactly the fields <see cref="BuildStepEntity"/> / <see cref="SyncImages"/>
        /// write. Omissions in the definition (null values) are considered a change if the
        /// existing record has a value, ensuring the Dataverse state matches the code.
        /// </summary>
        private bool StepUnchanged(PluginStepDefinition def, ExistingStep existing)
        {
            if (existing.Name != def.StepName) return false;
            if (existing.Rank != def.ExecutionOrder) return false;

            var desiredFiltering = def.FilteringAttributes.Length > 0
                ? string.Join(",", def.FilteringAttributes)
                : null;
            if (existing.FilteringAttributes != desiredFiltering) return false;

            if (NullIfEmpty(existing.Description) != NullIfEmpty(def.Description)) return false;
            if (NullIfEmpty(existing.Configuration) != NullIfEmpty(def.Configuration)) return false;

            var targetImpersonation = def.RunAsUser == Guid.Empty ? _systemUserId : def.RunAsUser;
            if (existing.ImpersonatingUserId != targetImpersonation) return false;

            return ImagesUnchanged(existing.StepId, def);
        }

        private bool ImagesUnchanged(Guid stepId, PluginStepDefinition def)
        {
            var desired = new HashSet<(ImageType, string, string?)>();
            foreach (var img in def.Images)
            {
                // PostImage on a non-PostOperation stage is skipped by SyncImages, so ignore it here too.
                if (img.ImageType == ImageType.Post && def.Stage != 40) continue;
                var attrs = img.Attributes.Length > 0 ? string.Join(",", img.Attributes) : null;
                desired.Add((img.ImageType, img.Alias, attrs));
            }

            var current = new HashSet<(ImageType, string, string?)>();
            foreach (var kvp in LoadExistingImagesWithAttributes(stepId))
                current.Add(kvp);

            return desired.SetEquals(current);
        }

        private HashSet<(ImageType, string, string?)> LoadExistingImagesWithAttributes(Guid stepId)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet("imagetype", "entityalias", "attributes"),
                Criteria = new FilterExpression()
            };
            query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);

            var set = new HashSet<(ImageType, string, string?)>();
            foreach (var e in _svc.RetrieveMultiple(query).Entities)
            {
                var type = (e.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0) == 0
                    ? ImageType.Pre : ImageType.Post;
                var alias = e.GetAttributeValue<string>("entityalias") ?? string.Empty;
                set.Add((type, alias, NullIfEmpty(e.GetAttributeValue<string>("attributes"))));
            }
            return set;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

        /// <summary>The distinct, non-global entity logical names targeted by the definitions.</summary>
        private static List<string> ReferencedEntityNames(IReadOnlyList<PluginStepDefinition> definitions) =>
            definitions.Select(d => d.Entity)
                       .Where(e => !string.IsNullOrWhiteSpace(e))
                       .Select(e => e.ToLowerInvariant())
                       .Distinct()
                       .ToList();

        // ── Image sync ─────────────────────────────────────────────────────────

        private void SyncImages(Guid stepId, PluginStepDefinition def)
        {
            var existingImages = LoadExistingImages(stepId);
            var desiredImages = new HashSet<(ImageType, string)>();

            foreach (var img in def.Images)
            {
                // PostImage only valid on PostOperation (stage 40)
                if (img.ImageType == ImageType.Post && def.Stage != 40)
                {
                    logger.LogWarning(
                        "PostImage requested on stage {Stage} for {Type} — only valid on PostOperation. Skipping.",
                        PluginStepDefinition.StageName(def.Stage), def.TypeFullName);
                    continue;
                }

                desiredImages.Add((img.ImageType, img.Alias));

                if (existingImages.TryGetValue((img.ImageType, img.Alias), out var existingId))
                {
                    _svc.Update(BuildImageEntity(img, stepId, existingId));
                }
                else
                {
                    _svc.Create(BuildImageEntity(img, stepId));
                }
            }

            // Delete images no longer desired
            foreach (var kvp in existingImages)
            {
                if (!desiredImages.Contains(kvp.Key))
                {
                    logger.LogInformation("Deleting orphan image: {Alias}", kvp.Key.Item2);
                    _svc.Delete("sdkmessageprocessingstepimage", kvp.Value);
                }
            }
        }

        private Dictionary<(ImageType, string), Guid> LoadExistingImages(Guid stepId)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid", "imagetype", "entityalias"),
                Criteria = new FilterExpression()
            };
            query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);

            var cache = new Dictionary<(ImageType, string), Guid>();
            foreach (var e in _svc.RetrieveMultiple(query).Entities)
            {
                var typeVal = e.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0;
                var alias = e.GetAttributeValue<string>("entityalias") ?? string.Empty;
                cache[(typeVal == 0 ? ImageType.Pre : ImageType.Post, alias)] = e.Id;
            }

            return cache;
        }

        private static Entity BuildImageEntity(ImageDefinition img, Guid stepId, Guid? existingId = null)
        {
            var e = existingId.HasValue
                ? new Entity("sdkmessageprocessingstepimage", existingId.Value)
                : new Entity("sdkmessageprocessingstepimage");

            e["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
            e["imagetype"] = new OptionSetValue((int)img.ImageType);
            e["name"] = img.Alias;
            e["entityalias"] = img.Alias;
            e["messagepropertyname"] = "Target";
            e["attributes"] = img.Attributes.Length > 0 ? string.Join(",", img.Attributes) : null;

            return e;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private bool IsCustomStepAllowed(Guid filterId)
        {
            var filter = _svc.Retrieve("sdkmessagefilter", filterId,
                new ColumnSet("iscustomprocessingstepallowed"));
            return filter.GetAttributeValue<bool>("iscustomprocessingstepallowed");
        }

        private static void ValidateImages(PluginStepDefinition def, SyncResult result)
        {
            foreach (var img in def.Images)
            {
                if (img.ImageType == ImageType.Post && def.Stage != 40)
                    result.Warnings.Add(
                        $"PostImage on {def.TypeFullName} ({PluginStepDefinition.StageName(def.Stage)}) will be skipped — only valid on PostOperation.");
            }
        }
    }
}