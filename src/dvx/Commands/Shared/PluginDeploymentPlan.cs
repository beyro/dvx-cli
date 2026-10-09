using dvx.Models;
using dvx.Services;

namespace dvx.Commands.Shared
{
    /// <summary>
    /// Selects the build artifact to upload for the requested <see cref="PluginBuildMode"/> and
    /// surfaces a warning when the mode and the produced artifacts disagree (E18).
    /// Strict — package mode fails when the build emits no <c>.nupkg</c> rather than falling back
    /// to the DLL.
    /// </summary>
    public sealed record PluginDeploymentPlan(string ArtifactPath, string? Warning)
    {
        public static PluginDeploymentPlan Resolve(PluginBuildMode mode, BuildResult build)
        {
            if (mode == PluginBuildMode.Package)
            {
                if (build.NupkgPath is null)
                    throw new InvalidOperationException(
                        "Package mode requires a .nupkg, but the build produced none. " +
                        "Use --plugin-build-mode assembly to deploy a bare plugin assembly.");

                return new(build.NupkgPath, null);
            }

            return new(build.DllPath,
                build.NupkgPath is not null
                    ? "Assembly mode selected, but the project also produced a .nupkg — deploying the DLL directly and ignoring the .nupkg."
                    : null);
        }

        /// <summary>
        /// True when a deployed assembly should be added to the target solution: assembly mode only,
        /// with a solution configured and a real (non-dry-run) assembly id.
        /// </summary>
        public static bool ShouldAddAssemblyToSolution(
            PluginBuildMode mode, string? solution, bool dryRun, Guid assemblyId)
            => mode == PluginBuildMode.Assembly
               && solution is not null
               && !dryRun
               && assemblyId != Guid.Empty;
    }
}
