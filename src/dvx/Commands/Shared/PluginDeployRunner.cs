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

            Guid assemblyId;
            if (mode == PluginBuildMode.Package)
            {
                assemblyId = new PackageDeployer(svc).Deploy(artifact, verbose, dryRun);
            }
            else
            {
                var typeNames = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance)
                    .DiscoverPluginTypeNames(build.DllPath);
                assemblyId = DeployAssembly(
                    new AssemblyDeployer(svc), new PluginTypeRegistrar(svc), artifact, typeNames, dryRun, verbose);
            }

            if (PluginDeploymentPlan.ShouldAddAssemblyToSolution(mode, solution, dryRun, assemblyId))
                new SolutionService(svc).AddAssemblyToSolution(assemblyId, solution!, verbose);

            Out.Success(dryRun ? "Resolved assembly (upload skipped — dry run)." : "Deployed.",
                $"Assembly ID: {assemblyId}");

            return (assemblyId, build.DllPath);
        }

        /// <summary>
        /// Deploys a bare assembly: reconciles its <c>plugintype</c> rows around the content update.
        /// A type whose class is no longer in the build must be removed <b>before</b> the update —
        /// Dataverse validates every registered type against the incoming assembly and rejects the
        /// update otherwise — and any new class is registered afterwards. Mirrors spkl / the PRT.
        /// </summary>
        internal static Guid DeployAssembly(
            AssemblyDeployer deployer,
            PluginTypeRegistrar registrar,
            PluginArtifact artifact,
            IReadOnlyList<string> typeNames,
            bool dryRun,
            bool verbose)
        {
            var existingId = deployer.FindExistingId(artifact.AssemblyName);
            if (existingId is not null)
                registrar.DeleteOrphans(existingId.Value, typeNames, dryRun, verbose);

            var assemblyId = deployer.Deploy(artifact, verbose, dryRun);

            if (!dryRun)
                registrar.EnsureRegistered(assemblyId, typeNames, verbose);

            return assemblyId;
        }
    }
}
