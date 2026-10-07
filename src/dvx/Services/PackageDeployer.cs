using dvx.Output;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace dvx.Services
{
    /// <summary>
    /// Pushes an updated plugin package to Dataverse by writing the base64-encoded
    /// <c>.nupkg</c> to the <c>content</c> column of the existing <c>pluginpackage</c> record
    /// (the same mechanism <c>pac plugin push</c> uses internally — no external CLI required).
    /// Only supports <b>updating</b> an existing package — the initial upload must be performed
    /// once manually (e.g. with the Plugin Registration Tool). After a successful update, returns
    /// the child <c>pluginassembly</c> ID for step registration.
    /// </summary>
    public class PackageDeployer : PluginDeployerBase
    {
        public PackageDeployer(IOrganizationService svc) : base(svc) { }

        /// <summary>
        /// Backward-compatible entry point taking the raw package paths used by the commands.
        /// Delegates to the shared <see cref="PluginDeployerBase.Deploy"/> skeleton.
        /// </summary>
        public Guid Deploy(string nupkgPath, string packageUniqueName, bool verbose = false, bool dryRun = false)
            => Deploy(new PluginArtifact(nupkgPath, packageUniqueName, packageUniqueName, null), verbose, dryRun);

        // ── Skeleton hooks ─────────────────────────────────────────────────────

        protected override Guid? ResolveExistingId(PluginArtifact artifact, bool verbose)
            => FindIdByUniqueName("pluginpackage", artifact.UniqueName)
               ?? throw new InvalidOperationException(
                    $"Plugin package '{artifact.UniqueName}' was not found in Dataverse. " +
                    "The initial upload must be done once manually (e.g. with the Plugin " +
                    "Registration Tool). Once the record exists, dvx can push updates to it.");

        protected override Guid? UploadContent(Guid? existingId, PluginArtifact artifact, bool verbose)
        {
            var bytes = File.ReadAllBytes(artifact.Path);

            if (verbose)
                Out.Dim($"    Uploading {bytes.Length / 1024} KB to pluginpackage {existingId}");

            Out.SubStep("Uploading package content...");

            // Only set content — name/uniquename/version are immutable once the package
            // exists; Dataverse re-extracts the assemblies and plugin types from the new
            // content on update.
            Svc.Update(new Entity("pluginpackage", existingId!.Value)
            {
                ["content"] = Convert.ToBase64String(bytes),
            });

            return existingId;
        }

        protected override Guid ReturnAssemblyId(Guid? id, PluginArtifact artifact)
            => FindAssemblyInPackage(id!.Value, artifact.UniqueName);

        // ── Dataverse queries ──────────────────────────────────────────────────

        private Guid FindAssemblyInPackage(Guid packageId, string packageUniqueName)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("packageid", ConditionOperator.Equal, packageId);
            var result = Svc.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"No pluginassembly child record found for package '{packageUniqueName}'. " +
                    "Ensure the initial package upload was fully processed by Dataverse.");

            return result.Entities[0].Id;
        }
    }
}
