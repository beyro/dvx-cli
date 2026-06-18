using dvx.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class PackageDeployerTests
    {
        private const string UniqueName = "solu_TestPlugin";

        // ── Mock builders ──────────────────────────────────────────────────────

        private static IOrganizationService BuildSvc(
            Guid? existingPackageId, Guid? assemblyId = null)
        {
            var svc = Substitute.For<IOrganizationService>();

            if (existingPackageId.HasValue)
            {
                var pkg = new Entity("pluginpackage", existingPackageId.Value);
                svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginpackage"))
                   .Returns(new EntityCollection(new List<Entity> { pkg }));
            }
            else
            {
                svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginpackage"))
                   .Returns(new EntityCollection());
            }

            if (assemblyId.HasValue)
            {
                var asm = new Entity("pluginassembly", assemblyId.Value);
                svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
                   .Returns(new EntityCollection(new List<Entity> { asm }));
            }
            else
            {
                svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "pluginassembly"))
                   .Returns(new EntityCollection());
            }

            return svc;
        }

        // Writes a throwaway .nupkg the deployer can read; caller deletes it.
        private static string WriteTempNupkg()
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nupkg");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            return path;
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Fact]
        public void Deploy_PackageNotFound_ThrowsWithHelpfulMessage()
        {
            var svc = BuildSvc(existingPackageId: null);

            // Package lookup fails before any file is read, so the path need not exist.
            var ex = Should.Throw<InvalidOperationException>(() =>
                new PackageDeployer(svc).Deploy("pkg.nupkg", UniqueName));

            ex.Message.ShouldContain(UniqueName);
            ex.Message.ShouldContain("initial upload");
        }

        [Fact]
        public void Deploy_PackageNotFound_UpdateNeverCalled()
        {
            var svc = BuildSvc(existingPackageId: null);

            Should.Throw<InvalidOperationException>(() =>
                new PackageDeployer(svc).Deploy("pkg.nupkg", UniqueName));

            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Deploy_HappyPath_UploadsContentAndReturnsAssemblyId()
        {
            var packageId  = Guid.NewGuid();
            var assemblyId = Guid.NewGuid();
            var svc   = BuildSvc(existingPackageId: packageId, assemblyId: assemblyId);
            var nupkg = WriteTempNupkg();

            try
            {
                var result = new PackageDeployer(svc).Deploy(nupkg, UniqueName);

                result.ShouldBe(assemblyId);
                svc.Received(1).Update(Arg.Is<Entity>(e =>
                    e.LogicalName == "pluginpackage" &&
                    e.Id == packageId &&
                    e.Contains("content")));
            }
            finally
            {
                File.Delete(nupkg);
            }
        }

        [Fact]
        public void Deploy_DryRun_DoesNotUpload_ButReturnsAssemblyId()
        {
            // A dry run must resolve the existing assembly id for the plan without writing content.
            var packageId  = Guid.NewGuid();
            var assemblyId = Guid.NewGuid();
            var svc   = BuildSvc(existingPackageId: packageId, assemblyId: assemblyId);
            var nupkg = WriteTempNupkg();

            try
            {
                var result = new PackageDeployer(svc).Deploy(nupkg, UniqueName, dryRun: true);

                result.ShouldBe(assemblyId);
                svc.DidNotReceive().Update(Arg.Any<Entity>());
            }
            finally
            {
                File.Delete(nupkg);
            }
        }

        [Fact]
        public void Deploy_UploadFails_Throws()
        {
            var packageId = Guid.NewGuid();
            var svc = BuildSvc(existingPackageId: packageId, assemblyId: Guid.NewGuid());
            svc.When(s => s.Update(Arg.Any<Entity>()))
               .Do(_ => throw new InvalidOperationException("upload failed"));
            var nupkg = WriteTempNupkg();

            try
            {
                Should.Throw<InvalidOperationException>(() =>
                    new PackageDeployer(svc).Deploy(nupkg, UniqueName));
            }
            finally
            {
                File.Delete(nupkg);
            }
        }

        [Fact]
        public void Deploy_AssemblyNotFoundAfterUpload_ThrowsContainingPackageName()
        {
            var packageId = Guid.NewGuid();
            var svc   = BuildSvc(existingPackageId: packageId, assemblyId: null); // no child assembly
            var nupkg = WriteTempNupkg();

            try
            {
                var ex = Should.Throw<InvalidOperationException>(() =>
                    new PackageDeployer(svc).Deploy(nupkg, UniqueName));

                ex.Message.ShouldContain(UniqueName);
            }
            finally
            {
                File.Delete(nupkg);
            }
        }
    }
}
