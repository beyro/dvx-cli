using dvx.Commands.Shared;
using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PluginDeployRunnerTests
    {
        private const string AssemblyName = "MyPlugin";

        private static IOrganizationService SvcWithExistingAssembly(Guid assemblyId, Guid orphanTypeId)
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(new List<Entity> { new Entity("pluginassembly", assemblyId) }));

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "plugintype"))
               .Returns(new EntityCollection(new List<Entity>
               {
                   new Entity("plugintype", orphanTypeId) { ["typename"] = "NS.Old" }
               }));

            return svc;
        }

        private static PluginArtifact Artifact(string dllPath) =>
            new(dllPath, AssemblyName, "prefix_" + AssemblyName, new Version(1, 0, 0, 0));

        private static string WriteTempDll()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return path;
        }

        [Fact]
        public void DeployAssembly_ExistingAssembly_DeletesOrphanTypeBeforeUpdatingContent()
        {
            var assemblyId   = Guid.NewGuid();
            var orphanTypeId = Guid.NewGuid();
            var svc          = SvcWithExistingAssembly(assemblyId, orphanTypeId);
            var dll          = WriteTempDll();

            try
            {
                PluginDeployRunner.DeployAssembly(
                    new AssemblyDeployer(svc), new PluginTypeRegistrar(svc),
                    Artifact(dll), new[] { "NS.New" }, dryRun: false, verbose: false);

                // Dataverse rejects the pluginassembly update while a stale plugintype remains, so
                // the orphan must be deleted first.
                Received.InOrder(() =>
                {
                    svc.Delete("plugintype", orphanTypeId);
                    svc.Update(Arg.Is<Entity>(e =>
                        e.LogicalName == "pluginassembly" && e.Id == assemblyId));
                });
            }
            finally
            {
                File.Delete(dll);
            }
        }

        [Fact]
        public void DeployAssembly_NewAssembly_DoesNotDeleteAnything()
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Any<QueryExpression>()).Returns(new EntityCollection());
            var dll = WriteTempDll();

            try
            {
                PluginDeployRunner.DeployAssembly(
                    new AssemblyDeployer(svc), new PluginTypeRegistrar(svc),
                    Artifact(dll), new[] { "NS.A" }, dryRun: false, verbose: false);

                svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
            }
            finally
            {
                File.Delete(dll);
            }
        }

        [Fact]
        public void DeployAssembly_DryRun_WritesNothing()
        {
            var assemblyId   = Guid.NewGuid();
            var orphanTypeId = Guid.NewGuid();
            var svc          = SvcWithExistingAssembly(assemblyId, orphanTypeId);
            var dll          = WriteTempDll();

            try
            {
                PluginDeployRunner.DeployAssembly(
                    new AssemblyDeployer(svc), new PluginTypeRegistrar(svc),
                    Artifact(dll), new[] { "NS.New" }, dryRun: true, verbose: false);

                svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
                svc.DidNotReceive().Update(Arg.Any<Entity>());
                svc.DidNotReceive().Create(Arg.Any<Entity>());
            }
            finally
            {
                File.Delete(dll);
            }
        }
    }
}
