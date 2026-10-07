using dvx.Commands.Shared;
using dvx.Models;
using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PluginDeploymentPlanTests
    {
        private static BuildResult Build(string? nupkg) => new(nupkg, @"C:\out\MyPlugin.dll");

        [Fact]
        public void PackageMode_WithNupkg_DeploysNupkg_NoWarning()
        {
            var plan = PluginDeploymentPlan.Resolve(PluginBuildMode.Package, Build(@"C:\out\MyPlugin.1.0.0.nupkg"));

            plan.ArtifactPath.ShouldBe(@"C:\out\MyPlugin.1.0.0.nupkg");
            plan.Warning.ShouldBeNull();
        }

        [Fact]
        public void PackageMode_WithoutNupkg_Throws()
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                PluginDeploymentPlan.Resolve(PluginBuildMode.Package, Build(null)));

            ex.Message.ShouldContain(".nupkg");
            ex.Message.ShouldContain("assembly");
        }

        [Fact]
        public void AssemblyMode_WithoutNupkg_DeploysDll_NoWarning()
        {
            var plan = PluginDeploymentPlan.Resolve(PluginBuildMode.Assembly, Build(null));

            plan.ArtifactPath.ShouldBe(@"C:\out\MyPlugin.dll");
            plan.Warning.ShouldBeNull();
        }

        [Fact]
        public void AssemblyMode_WithNupkg_DeploysDll_WithWarning()
        {
            var plan = PluginDeploymentPlan.Resolve(PluginBuildMode.Assembly, Build(@"C:\out\MyPlugin.1.0.0.nupkg"));

            plan.ArtifactPath.ShouldBe(@"C:\out\MyPlugin.dll");
            plan.Warning.ShouldNotBeNull();
            plan.Warning!.ShouldContain(".nupkg");
        }
    }
}
