using dvx.Output;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Deploys a bare plugin assembly by writing the base64-encoded <c>.dll</c> to the
    /// <c>content</c> column of a <c>pluginassembly</c> record — creating the record when no
    /// assembly with the same <c>name</c> exists, otherwise updating its content. Returns the
    /// record's own id for step registration. Only the required attributes are set; none are
    /// configurable.
    /// </summary>
    public class AssemblyDeployer : PluginDeployerBase
    {
        public AssemblyDeployer(IOrganizationService svc) : base(svc) { }

        // ── Skeleton hooks ─────────────────────────────────────────────────────

        protected override Guid? ResolveExistingId(PluginArtifact artifact, bool verbose)
            => FindExistingId(artifact.AssemblyName);

        /// <summary>
        /// Resolves the <c>pluginassembly</c> id by <c>name</c>, or null when none exists.
        /// Throws if more than one record shares the name.
        /// </summary>
        public Guid? FindExistingId(string assemblyName)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, assemblyName);
            var result = Svc.RetrieveMultiple(query);

            if (result.Entities.Count > 1)
                throw new InvalidOperationException(
                    $"Multiple pluginassembly records named '{assemblyName}' found in Dataverse. " +
                    "Remove the duplicates before deploying.");

            return result.Entities.Count == 1 ? result.Entities[0].Id : null;
        }

        protected override Guid? UploadContent(Guid? existingId, PluginArtifact artifact, bool verbose)
        {
            var bytes = File.ReadAllBytes(artifact.Path);

            if (verbose)
                Out.Dim($"    Uploading {bytes.Length / 1024} KB to pluginassembly");

            var content = Convert.ToBase64String(bytes);

            if (existingId is null)
            {
                Out.SubStep("Creating plugin assembly...");

                var id = Svc.Create(new Entity("pluginassembly")
                {
                    ["name"]          = artifact.AssemblyName,
                    ["sourcetype"]    = new OptionSetValue(0),   // Database
                    ["isolationmode"] = new OptionSetValue(2),   // Sandbox
                    ["version"]       = artifact.Version!.ToString(),
                    ["culture"]       = "neutral",
                    ["content"]       = content,
                });
                return id;
            }

            Out.SubStep("Updating plugin assembly content...");

            Svc.Update(new Entity("pluginassembly", existingId.Value)
            {
                ["content"] = content,
                ["version"] = artifact.Version!.ToString(),
            });
            return existingId;
        }

        protected override Guid ReturnAssemblyId(Guid? id, PluginArtifact artifact)
        {
            if (id is null)
            {
                // Only reachable on a dry run against a not-yet-created assembly.
                Out.DryRun($"Assembly '{artifact.AssemblyName}' not found — would create.");
                return Guid.Empty;
            }

            return id.Value;
        }
    }
}
