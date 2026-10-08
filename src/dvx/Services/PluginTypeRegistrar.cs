using dvx.Output;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Reconciles the <c>plugintype</c> rows for a deployed assembly. A bare <c>pluginassembly</c>
    /// upload does not create them (unlike a <c>pluginpackage</c>, which Dataverse extracts itself),
    /// so dvx registers each plugin class itself — mirroring the Plugin Registration Tool / spkl —
    /// and, on request, removes rows whose class is no longer present.
    /// </summary>
    public class PluginTypeRegistrar(IOrganizationService svc)
    {
        /// <summary>
        /// Creates a <c>plugintype</c> for every desired type that does not already exist on the assembly.
        /// </summary>
        public void EnsureRegistered(Guid assemblyId, IReadOnlyList<string> desiredTypeNames, bool verbose = false)
        {
            if (desiredTypeNames.Count == 0)
                return;

            var existing = ExistingTypes(assemblyId).Select(t => t.TypeName).ToHashSet(StringComparer.Ordinal);

            foreach (var typeName in desiredTypeNames.Distinct(StringComparer.Ordinal))
            {
                if (existing.Contains(typeName))
                    continue;

                if (verbose)
                    Out.Dim($"    Registering plugin type '{typeName}'");

                svc.Create(new Entity("plugintype")
                {
                    ["name"]             = typeName,
                    ["typename"]         = typeName,
                    ["friendlyname"]     = typeName,
                    ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
                });
            }
        }

        /// <summary>
        /// Deletes the assembly's <c>plugintype</c> rows whose <c>typename</c> is absent from the
        /// desired set, deleting each orphan's steps first. Types backing a Custom API or Custom
        /// Action are never removed. No writes on a dry run.
        /// </summary>
        public void DeleteOrphans(Guid assemblyId, IReadOnlyList<string> desiredTypeNames,
                                  bool dryRun = false, bool verbose = false)
        {
            var existing = ExistingTypes(assemblyId);
            if (existing.Count == 0)
                return;

            // Safety rail: an empty desired set usually means reflection/load failed, not that the
            // assembly genuinely has no plugin classes — refuse to delete everything.
            if (desiredTypeNames.Count == 0)
            {
                Out.Warn("No plugin types were found in the build — skipping orphaned type deletion.");
                return;
            }

            var desired      = new HashSet<string>(desiredTypeNames, StringComparer.Ordinal);
            var protectedIds = ProtectedTypeIds();

            var deletable = new List<ExistingType>();
            foreach (var type in existing)
            {
                if (desired.Contains(type.TypeName))
                    continue;

                if (protectedIds.Contains(type.Id))
                {
                    Out.Warn($"Keeping plugin type '{type.TypeName}' — it backs a Custom API or Custom Action.");
                    continue;
                }

                deletable.Add(type);
            }

            foreach (var type in deletable)
            {
                if (verbose)
                    Out.Dim($"    Deleting orphaned plugin type '{type.TypeName}'");

                if (dryRun)
                    continue;

                DeleteStepsFor(type.Id);
                svc.Delete("plugintype", type.Id);
            }

            if (deletable.Count == 0)
                return;

            if (dryRun)
                Out.DryRun($"Would prune {deletable.Count} orphaned plugin type(s).");
            else
                Out.Success("Pruned", $"{deletable.Count} orphaned plugin type(s).");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private record ExistingType(Guid Id, string TypeName);

        private List<ExistingType> ExistingTypes(Guid assemblyId)
        {
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("plugintypeid", "typename"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);

            return svc.RetrieveMultiple(query).Entities
                .Select(e => new ExistingType(e.Id, e.GetAttributeValue<string>("typename") ?? string.Empty))
                .ToList();
        }

        private HashSet<Guid> ProtectedTypeIds()
        {
            var meta = new SdkMetadata(svc);
            var set  = meta.CustomApiPluginTypeIds();
            set.UnionWith(meta.CustomActionPluginTypeIds());
            return set;
        }

        private void DeleteStepsFor(Guid pluginTypeId)
        {
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("plugintypeid", ConditionOperator.Equal, pluginTypeId);

            foreach (var e in svc.RetrieveMultiple(query).Entities)
                svc.Delete("sdkmessageprocessingstep", e.Id);
        }
    }
}
