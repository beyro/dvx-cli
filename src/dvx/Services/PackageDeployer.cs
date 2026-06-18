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
    /// once manually (e.g. with the Plugin Registration Tool). After a successful update,
    /// returns the child <c>pluginassembly</c> ID for step registration.
    /// </summary>
    public class PackageDeployer
    {
        private readonly IOrganizationService _svc;

        public PackageDeployer(IOrganizationService svc) => _svc = svc;

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Looks up the existing <c>pluginpackage</c> record, uploads the new package content
        /// via an <see cref="IOrganizationService.Update"/> call, then returns the child
        /// <c>pluginassembly</c> ID. Dataverse re-extracts the assemblies and plugin types
        /// from the uploaded package automatically.
        /// </summary>
        public Guid Deploy(string nupkgPath, string packageUniqueName, bool verbose = false, bool dryRun = false)
        {
            var packageId = FindExistingPackage(packageUniqueName)
                ?? throw new InvalidOperationException(
                    $"Plugin package '{packageUniqueName}' was not found in Dataverse. " +
                    "The initial upload must be done once manually (e.g. with the Plugin " +
                    "Registration Tool). Once the record exists, dvx can push updates to it.");

            if (!dryRun)
            {
                UploadContent(nupkgPath, packageId, verbose);
            }

            return FindAssemblyInPackage(packageId, packageUniqueName);
        }

        // ── Upload ─────────────────────────────────────────────────────────────

        private void UploadContent(string nupkgPath, Guid packageId, bool verbose)
        {
            var bytes = File.ReadAllBytes(nupkgPath);

            if (verbose)
                Out.Dim($"    Uploading {bytes.Length / 1024} KB to pluginpackage {packageId}");

            Out.SubStep("Uploading package content...");

            // Only set content — name/uniquename/version are immutable once the package
            // exists; Dataverse re-extracts the assemblies and plugin types from the new
            // content on update.
            _svc.Update(new Entity("pluginpackage", packageId)
            {
                ["content"] = Convert.ToBase64String(bytes),
            });
        }

        // ── Dataverse queries ──────────────────────────────────────────────────

        private Guid? FindExistingPackage(string uniqueName)
        {
            var query = new QueryExpression("pluginpackage")
            {
                ColumnSet = new ColumnSet("pluginpackageid"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);
            var result = _svc.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0].Id : null;
        }

        private Guid FindAssemblyInPackage(Guid packageId, string packageUniqueName)
        {
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet("pluginassemblyid"),
                Criteria  = new FilterExpression(),
            };
            query.Criteria.AddCondition("packageid", ConditionOperator.Equal, packageId);
            var result = _svc.RetrieveMultiple(query);

            if (result.Entities.Count == 0)
                throw new InvalidOperationException(
                    $"No pluginassembly child record found for package '{packageUniqueName}'. " +
                    "Ensure the initial package upload was fully processed by Dataverse.");

            return result.Entities[0].Id;
        }
    }
}
