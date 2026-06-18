using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    public class AssemblyDownloader(IOrganizationService svc)
    {
        /// <summary>
        /// Resolves the id of the pluginassembly record with the given name.
        /// Throws when no matching assembly exists in Dataverse.
        /// </summary>
        public Guid FindId(string assemblyName)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, assemblyName);

            var result = svc.RetrieveMultiple(query);
            if (result.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"No pluginassembly named '{assemblyName}' found in Dataverse. Run 'dvx deploy' first.");

            return result.Entities[0].Id;
        }

        /// <summary>
        /// Downloads the DLL bytes from pluginassembly.content and writes to a temp file.
        /// Returns (assemblyId, tempDllPath).
        /// </summary>
        public (Guid AssemblyId, string DllPath) Download(string assemblyName)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid", "content", "sourcetype", "name"),
                Criteria  = new FilterExpression()
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, assemblyName);

            var results = svc.RetrieveMultiple(query);
            if (results.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"No pluginassembly named '{assemblyName}' found in Dataverse.");

            var record    = results.Entities[0];
            var sourceType = record.GetAttributeValue<OptionSetValue>("sourcetype")?.Value ?? 0;

            if (sourceType != 0)
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' has sourcetype={sourceType} (not Database). " +
                    $"Its content bytes are not stored in Dataverse. Use --project instead.");

            var content = record.GetAttributeValue<string>("content");
            if (string.IsNullOrEmpty(content))
                throw new InvalidOperationException(
                    $"Assembly '{assemblyName}' has no content in Dataverse. Use --project instead.");

            var bytes   = Convert.FromBase64String(content);
            var tempDir = Path.Combine(Path.GetTempPath(), $"dvx-asm-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            var dllPath = Path.Combine(tempDir, assemblyName + ".dll");
            File.WriteAllBytes(dllPath, bytes);

            return (record.Id, dllPath);
        }
    }
}
