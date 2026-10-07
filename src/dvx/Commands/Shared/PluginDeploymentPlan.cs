using dvx.Models;
using dvx.Services;

namespace dvx.Commands.Shared
{
    /// <summary>
    /// Maps a requested <see cref="PluginBuildMode"/> and the artifacts a build produced onto the
    /// mode and artifact actually deployed, warning when the two disagree (e.g. package mode
    /// selected for a project that emitted no <c>.nupkg</c>).
    /// </summary>
    public sealed record PluginDeploymentPlan(PluginBuildMode Mode, string ArtifactPath, string? Warning)
    {
        public static PluginDeploymentPlan Resolve(PluginBuildMode requested, BuildResult build)
        {
            if (requested == PluginBuildMode.Package && build.NupkgPath is not null)
                return new(PluginBuildMode.Package, build.NupkgPath, null);

            if (requested == PluginBuildMode.Package)
                return new(PluginBuildMode.Assembly, build.DllPath,
                    "Package mode selected, but the project produced no .nupkg — deploying the DLL as a plugin assembly instead.");

            if (build.NupkgPath is not null)
                return new(PluginBuildMode.Assembly, build.DllPath,
                    "Assembly mode selected, but the project also produced a .nupkg — deploying the DLL directly and ignoring the .nupkg.");

            return new(PluginBuildMode.Assembly, build.DllPath, null);
        }
    }
}
