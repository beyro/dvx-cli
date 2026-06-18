using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class AssemblyDownloaderTests
    {
        private const string AssemblyName = "MyPlugin";

        private static IOrganizationService BuildSvc(
            bool   found       = true,
            int    sourceType  = 0,
            string? content    = null)
        {
            var svc = Substitute.For<IOrganizationService>();

            if (!found)
            {
                svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
                   .Returns(new EntityCollection());
                return svc;
            }

            // Default content: a few bytes encoded as base64
            var contentValue = content ?? Convert.ToBase64String(new byte[] { 0x4D, 0x5A, 0x00 });

            var record = new Entity("pluginassembly", Guid.NewGuid())
            {
                ["name"]       = AssemblyName,
                ["sourcetype"] = new OptionSetValue(sourceType),
                ["content"]    = contentValue
            };

            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(new List<Entity> { record }));

            return svc;
        }

        // ── Happy path ─────────────────────────────────────────────────────────

        [Fact]
        public void AssemblyFound_WritesTempFile_ReturnsCorrectId()
        {
            var expectedBytes = new byte[] { 0x4D, 0x5A, 0xFF };
            var svc    = BuildSvc(content: Convert.ToBase64String(expectedBytes));
            var result = new AssemblyDownloader(svc).Download(AssemblyName);

            File.Exists(result.DllPath).ShouldBeTrue();
            File.ReadAllBytes(result.DllPath).ShouldBe(expectedBytes);
        }

        [Fact]
        public void AssemblyFound_ReturnsCorrectGuid()
        {
            // Capture the entity id from the mock
            var entityId = Guid.NewGuid();
            var svc      = Substitute.For<IOrganizationService>();
            var record   = new Entity("pluginassembly", entityId)
            {
                ["name"]       = AssemblyName,
                ["sourcetype"] = new OptionSetValue(0),
                ["content"]    = Convert.ToBase64String(new byte[] { 1, 2, 3 })
            };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(new List<Entity> { record }));

            var (assemblyId, _) = new AssemblyDownloader(svc).Download(AssemblyName);

            assemblyId.ShouldBe(entityId);
        }

        // ── Error cases ────────────────────────────────────────────────────────

        [Fact]
        public void AssemblyNotFound_ThrowsInvalidOperationException_ContainsName()
        {
            var svc = BuildSvc(found: false);
            var ex  = Should.Throw<InvalidOperationException>(() =>
                new AssemblyDownloader(svc).Download(AssemblyName));

            ex.Message.ShouldContain(AssemblyName);
        }

        [Fact]
        public void NonDatabaseSourceType_ThrowsInvalidOperationException()
        {
            var svc = BuildSvc(sourceType: 1); // 1 = Disk, not Database
            Should.Throw<InvalidOperationException>(() =>
                new AssemblyDownloader(svc).Download(AssemblyName));
        }

        [Fact]
        public void NullContent_ThrowsInvalidOperationException()
        {
            var svc = Substitute.For<IOrganizationService>();
            var record = new Entity("pluginassembly", Guid.NewGuid())
            {
                ["name"]       = AssemblyName,
                ["sourcetype"] = new OptionSetValue(0),
                ["content"]    = null
            };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(new List<Entity> { record }));

            Should.Throw<InvalidOperationException>(() =>
                new AssemblyDownloader(svc).Download(AssemblyName));
        }

        [Fact]
        public void EmptyContent_ThrowsInvalidOperationException()
        {
            var svc = BuildSvc(content: string.Empty);
            Should.Throw<InvalidOperationException>(() =>
                new AssemblyDownloader(svc).Download(AssemblyName));
        }

        // ── FindId ─────────────────────────────────────────────────────────────

        [Fact]
        public void FindId_AssemblyFound_ReturnsId()
        {
            var entityId = Guid.NewGuid();
            var svc      = Substitute.For<IOrganizationService>();
            var record   = new Entity("pluginassembly", entityId) { ["name"] = AssemblyName };
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
               .Returns(new EntityCollection(new List<Entity> { record }));

            new AssemblyDownloader(svc).FindId(AssemblyName).ShouldBe(entityId);
        }

        [Fact]
        public void FindId_NotFound_ThrowsInvalidOperationException_ContainsName()
        {
            var svc = BuildSvc(found: false);
            var ex  = Should.Throw<InvalidOperationException>(() =>
                new AssemblyDownloader(svc).FindId(AssemblyName));

            ex.Message.ShouldContain(AssemblyName);
        }
    }
}
