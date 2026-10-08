using dvx.Output;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Ensures a <c>plugintype</c> row exists for every plugin class in a deployed assembly.
    /// Unlike a <c>pluginpackage</c> upload — where Dataverse extracts the types itself — a bare
    /// <c>pluginassembly</c> upload does not create the plugin types, so the registering tool must
    /// (this mirrors the Plugin Registration Tool and spkl). Existing types are left untouched;
    /// only missing ones are created.
    /// </summary>
    public class PluginTypeRegistrar(IOrganizationService svc)
    {
        public void EnsureRegistered(Guid assemblyId, IReadOnlyList<string> typeNames, bool verbose = false)
        {
            if (typeNames.Count == 0)
                return;

            var existing = ExistingTypeNames(assemblyId);

            foreach (var typeName in typeNames.Distinct(StringComparer.Ordinal))
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

        private HashSet<string> ExistingTypeNames(Guid assemblyId)
        {
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet("typename"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in svc.RetrieveMultiple(query).Entities)
            {
                var name = e.GetAttributeValue<string>("typename");
                if (name is not null) set.Add(name);
            }
            return set;
        }
    }
}
