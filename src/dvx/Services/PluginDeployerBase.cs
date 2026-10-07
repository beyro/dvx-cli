using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Shared deployment skeleton for the two Dataverse plugin targets:
    /// resolve the target record → upload the artifact content (skipped on dry runs) →
    /// return the <c>pluginassembly</c> id used for downstream step registration.
    /// </summary>
    public abstract class PluginDeployerBase : IPluginDeployer
    {
        protected IOrganizationService Svc { get; }

        protected PluginDeployerBase(IOrganizationService svc) => Svc = svc;

        public Guid Deploy(PluginArtifact artifact, bool verbose = false, bool dryRun = false)
        {
            var existingId = ResolveExistingId(artifact, verbose);
            var id = dryRun ? existingId : UploadContent(existingId, artifact, verbose);
            return ReturnAssemblyId(id, artifact);
        }

        /// <summary>Resolves the existing target record id, or null when none exists (create path).</summary>
        protected abstract Guid? ResolveExistingId(PluginArtifact artifact, bool verbose);

        /// <summary>Uploads the artifact content, creating a new record when <paramref name="existingId"/> is null.</summary>
        protected abstract Guid? UploadContent(Guid? existingId, PluginArtifact artifact, bool verbose);

        /// <summary>Returns the assembly id downstream step registration should target.</summary>
        protected abstract Guid ReturnAssemblyId(Guid? id, PluginArtifact artifact);

        /// <summary>Finds the id of the record whose <c>uniquename</c> matches, or null.</summary>
        protected Guid? FindIdByUniqueName(string entityName, string uniqueName)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet($"{entityName}id"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);
            var result = Svc.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0].Id : null;
        }
    }
}
