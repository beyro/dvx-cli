using System.Reflection;
using dvx.Models;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;

namespace dvx.Commands.Shared
{
    /// <summary>
    /// Shared build-and-deploy step for <c>plugin deploy</c> and <c>plugin sync</c>: build the
    /// project, resolve the artifact for the mode, pick the deployer, deploy it, and add the
    /// assembly to the target solution in assembly mode.
    /// </summary>
    public static class PluginDeployRunner
    {
        /// <summary>
        /// Builds and deploys, returning the assembly id and the built DLL path (used for reflection).
        /// </summary>
        public static (Guid AssemblyId, string DllPath) BuildAndDeploy(
            IOrganizationService svc,
            PluginBuildMode mode,
            string? solution,
            string url,
            string project,
            string prefix,
            bool verbose,
            bool dryRun)
        {
            Out.Step("Building", project);
            var build = new ProjectBuilder().BuildAllowingMissingPackage(project);
            var plan  = PluginDeploymentPlan.Resolve(mode, build);
            if (plan.Warning is not null) Out.Warn(plan.Warning);

            var assemblyName = Path.GetFileNameWithoutExtension(build.DllPath);
            var uniqueName   = $"{prefix}_{assemblyName}";
            // The version is only consumed in assembly mode; reading it opens the DLL, so skip it
            // entirely on the package path (which must stay behaviourally identical).
            var version      = mode == PluginBuildMode.Assembly
                ? AssemblyName.GetAssemblyName(build.DllPath).Version
                : null;
            var artifact     = new PluginArtifact(plan.ArtifactPath, assemblyName, uniqueName, version);
            Out.Success("Built", Path.GetFileName(plan.ArtifactPath));

            Out.Step("Deploying", $"to {url}");
            IPluginDeployer deployer = mode == PluginBuildMode.Package
                ? new PackageDeployer(svc)
                : new AssemblyDeployer(svc);
            var assemblyId = deployer.Deploy(artifact, verbose, dryRun);

            // A bare pluginassembly upload does not create the plugintype rows (unlike a
            // pluginpackage, which Dataverse extracts itself), so register them here — the same
            // job the Plugin Registration Tool does — before steps are reconciled.
            if (mode == PluginBuildMode.Assembly && !dryRun)
            {
                var typeNames = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance)
                    .DiscoverPluginTypeNames(build.DllPath);
                new PluginTypeRegistrar(svc).EnsureRegistered(assemblyId, typeNames, verbose);
            }

            if (PluginDeploymentPlan.ShouldAddAssemblyToSolution(mode, solution, dryRun, assemblyId))
                new SolutionService(svc).AddAssemblyToSolution(assemblyId, solution!, verbose);

            Out.Success(dryRun ? "Resolved assembly (upload skipped — dry run)." : "Deployed.",
                $"Assembly ID: {assemblyId}");

            return (assemblyId, build.DllPath);
        }
    }
}
