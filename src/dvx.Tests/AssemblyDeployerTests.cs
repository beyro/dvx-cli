using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class AssemblyDeployerTests
    {
        private const string AssemblyName = "MyPlugin";

        private static PluginArtifact Artifact(string dllPath) =>
            new(dllPath, AssemblyName, AssemblyName, new Version(1, 2, 3, 4));

        private static IOrganizationService BuildSvc(params Guid[] existingIds)
        {
            var svc = Substitute.For<IOrganizationService>();
            var entities = existingIds.Select(id => new Entity("pluginassembly", id)).ToList();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(entities));
            return svc;
        }

        private static string WriteTempDll()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return path;
        }

        [Fact]
        public void Deploy_NotFound_CreatesRecord_ReturnsNewId()
        {
            var svc   = BuildSvc();
            var newId = Guid.NewGuid();
            svc.Create(Arg.Any<Entity>()).Returns(newId);
            var dll = WriteTempDll();

            try
            {
                var result = new AssemblyDeployer(svc).Deploy(Artifact(dll));
                var bytes  = File.ReadAllBytes(dll);

                result.ShouldBe(newId);
                svc.Received(1).Create(Arg.Is<Entity>(e =>
                    e.LogicalName == "pluginassembly" &&
                    (string)e["name"] == AssemblyName &&
                    ((OptionSetValue)e["sourcetype"]).Value == 0 &&
                    ((OptionSetValue)e["isolationmode"]).Value == 2 &&
                    (string)e["culture"] == "neutral" &&
                    (string)e["version"] == "1.2.3.4" &&
                    (string)e["content"] == Convert.ToBase64String(bytes)));
                svc.DidNotReceive().Update(Arg.Any<Entity>());
            }
            finally
            {
                File.Delete(dll);
            }
        }

        [Fact]
        public void Deploy_Found_UpdatesContent_ReturnsExistingId()
        {
            var existingId = Guid.NewGuid();
            var svc = BuildSvc(existingId);
            var dll = WriteTempDll();

            try
            {
                var result = new AssemblyDeployer(svc).Deploy(Artifact(dll));
                var bytes  = File.ReadAllBytes(dll);

                result.ShouldBe(existingId);
                svc.Received(1).Update(Arg.Is<Entity>(e =>
                    e.LogicalName == "pluginassembly" &&
                    e.Id == existingId &&
                    (string)e["version"] == "1.2.3.4" &&
                    (string)e["content"] == Convert.ToBase64String(bytes)));
                svc.DidNotReceive().Create(Arg.Any<Entity>());
            }
            finally
            {
                File.Delete(dll);
            }
        }

        [Fact]
        public void Deploy_MultipleMatches_Throws()
        {
            var svc = BuildSvc(Guid.NewGuid(), Guid.NewGuid());

            // Throws during the name lookup, before any file read — path need not exist.
            var ex = Should.Throw<InvalidOperationException>(() =>
                new AssemblyDeployer(svc).Deploy(Artifact("nonexistent.dll")));

            ex.Message.ShouldContain(AssemblyName);
        }

        [Fact]
        public void Deploy_DryRun_NotFound_WritesNothing_ReturnsEmpty()
        {
            var svc = BuildSvc();

            // No existing record and no write path in a dry run — path need not exist.
            var result = new AssemblyDeployer(svc).Deploy(Artifact("nonexistent.dll"), dryRun: true);

            result.ShouldBe(Guid.Empty);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Deploy_DryRun_Found_NoWrites_ReturnsExistingId()
        {
            var existingId = Guid.NewGuid();
            var svc = BuildSvc(existingId);

            var result = new AssemblyDeployer(svc).Deploy(Artifact("nonexistent.dll"), dryRun: true);

            result.ShouldBe(existingId);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }
    }
}
