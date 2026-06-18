using dvx.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Reads the SDK message processing steps already registered on a plugin assembly in Dataverse
    /// and reconstructs <see cref="PluginStepDefinition"/>s from them — the inverse of
    /// <see cref="StepRegistrar"/>. Used by the <c>adopt</c> command to scaffold [PluginStep]
    /// attributes onto an existing project.
    /// </summary>
    public class StepImporter(IOrganizationService svc, ILogger<StepImporter> logger)
    {
        public ImportResult Import(Guid assemblyId, bool verbose = false)
        {
            var result = new ImportResult();
            var meta   = new SdkMetadata(svc);

            var typeNameById = meta.PluginTypeNameById(assemblyId);
            if (typeNameById.Count == 0)
            {
                result.Warnings.Add(
                    $"No plugintype records found for assembly {assemblyId}. " +
                    "Ensure the assembly is deployed to this environment.");
                return result;
            }

            var messageNameById  = meta.MessageNameById();
            var filterEntityById = meta.FilterEntityById();

            foreach (var step in LoadSteps(typeNameById.Keys))
            {
                var stepName = step.GetAttributeValue<string>("name") ?? step.Id.ToString();

                var typeRef = step.GetAttributeValue<EntityReference>("plugintypeid");
                if (typeRef is null || !typeNameById.TryGetValue(typeRef.Id, out var typeName))
                    continue; // query is scoped to these plugin types, so this should not happen

                var msgRef = step.GetAttributeValue<EntityReference>("sdkmessageid");
                if (msgRef is null || !messageNameById.TryGetValue(msgRef.Id, out var message))
                {
                    result.Warnings.Add($"Step '{stepName}' on {typeName}: message could not be resolved — skipping.");
                    continue;
                }

                // Entity-specific steps reference an sdkmessagefilter. Global messages such as
                // Associate / Disassociate carry no filter and adopt with an empty entity, which
                // [PluginStep] represents via its empty-string entity argument — the inverse of
                // StepRegistrar writing a null sdkmessagefilterid for these steps.
                var filterRef = step.GetAttributeValue<EntityReference>("sdkmessagefilterid");
                var entity = filterRef is not null && filterEntityById.TryGetValue(filterRef.Id, out var e)
                    ? e
                    : string.Empty;

                var def = new PluginStepDefinition
                {
                    TypeFullName        = typeName,
                    Entity              = entity,
                    Message             = message,
                    Stage               = step.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
                    Mode                = step.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
                    ExecutionOrder      = step.GetAttributeValue<int>("rank"),
                    Description         = NullIfEmpty(step.GetAttributeValue<string>("description")),
                    FilteringAttributes = SplitCsv(step.GetAttributeValue<string>("filteringattributes")),
                    Configuration       = NullIfEmpty(step.GetAttributeValue<string>("configuration")),
                    RunAsUser           = ResolveImpersonation(step.GetAttributeValue<EntityReference>("impersonatinguserid"), meta.SystemUserId()),
                };

                foreach (var img in LoadImages(step.Id))
                {
                    var isPre = (img.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0) == 0;
                    def.Images.Add(new ImageDefinition
                    {
                        ImageType  = isPre ? ImageType.Pre : ImageType.Post,
                        Alias      = img.GetAttributeValue<string>("entityalias")
                                     ?? (isPre ? "PreImage" : "PostImage"),
                        Attributes = SplitCsv(img.GetAttributeValue<string>("attributes")),
                    });
                }

                if (verbose)
                    logger.LogInformation("Imported step {Name} → {Type} ({Entity}/{Message})",
                        stepName, typeName,
                        string.IsNullOrEmpty(entity) ? "(global)" : entity, message);

                result.Definitions.Add(def);
            }

            return result;
        }

        private List<Entity> LoadSteps(IEnumerable<Guid> pluginTypeIds)
        {
            var ids = pluginTypeIds.Cast<object>().ToArray();
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(
                    "sdkmessageprocessingstepid", "name", "plugintypeid", "sdkmessageid",
                    "sdkmessagefilterid", "stage", "mode", "rank", "filteringattributes",
                    "description", "impersonatinguserid", "configuration"),
                Criteria = new FilterExpression()
            };
            query.Criteria.AddCondition("plugintypeid", ConditionOperator.In, ids);
            return svc.RetrieveMultiple(query).Entities.ToList();
        }

        private List<Entity> LoadImages(Guid stepId)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet("imagetype", "entityalias", "attributes"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId);
            return svc.RetrieveMultiple(query).Entities.ToList();
        }

        /// <summary>
        /// Maps a step's impersonatinguserid back to the definition's RunAsUser:
        /// no reference → null (calling user); system user → Guid.Empty (RunAsSystem);
        /// a specific user id → that id.
        /// </summary>
        private static Guid? ResolveImpersonation(EntityReference? impersonating, Guid systemUserId)
        {
            if (impersonating is null) return null;
            if (impersonating.Id == systemUserId) return Guid.Empty;
            return impersonating.Id == Guid.Empty ? null : impersonating.Id;
        }

        private static string[] SplitCsv(string? value)
            => string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static string? NullIfEmpty(string? value)
            => string.IsNullOrEmpty(value) ? null : value;
    }
}
