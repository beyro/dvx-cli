using System.Text;
using dvx.Models;
using dvx.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class WebResourceSyncerTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dvx-wr-tests-{Guid.NewGuid():N}");

        public WebResourceSyncerTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private string WriteText(string name, string content)
        {
            var path = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        /// <summary>Mock service whose webresource query returns <paramref name="existing"/> (or empty).</summary>
        private static IOrganizationService SvcWith(EntityCollection? existing = null)
        {
            var svc = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "webresource"))
               .Returns(existing ?? new EntityCollection());
            svc.Create(Arg.Any<Entity>()).Returns(_ => Guid.NewGuid());
            return svc;
        }

        private static EntityCollection One(Guid id, string base64Content)
            => new EntityCollection(new List<Entity>
               {
                   new Entity("webresource", id) { ["content"] = base64Content }
               });

        private static WebResourceSyncer MakeSyncer(IOrganizationService svc, SolutionService? sol = null)
            => new WebResourceSyncer(svc, NullLogger<WebResourceSyncer>.Instance, sol);

        private static WebResourceDefinition Def(string name, string localPath, int? type = null, string? display = null)
            => new WebResourceDefinition { Name = name, LocalPath = localPath, Type = type, DisplayName = display };

        // ── Create / update / skip ─────────────────────────────────────────────

        [Fact]
        public void Missing_CreatesWebResourceWithInferredType()
        {
            var path = WriteText("main.js", "console.log(1);");
            var svc  = SvcWith(); // not found

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            result.Created.ShouldBe(1);
            svc.Received(1).Create(Arg.Is<Entity>(e =>
                e.LogicalName == "webresource" &&
                e.GetAttributeValue<string>("name") == "pub_/main.js" &&
                e.GetAttributeValue<OptionSetValue>("webresourcetype").Value == 3 &&
                e.GetAttributeValue<string>("content") == B64("console.log(1);")));
        }

        [Fact]
        public void Missing_UsesNameAsDisplayName_WhenNotProvided()
        {
            var path = WriteText("main.js", "x");
            var svc  = SvcWith();

            MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            svc.Received(1).Create(Arg.Is<Entity>(e =>
                e.GetAttributeValue<string>("displayname") == "pub_/main.js"));
        }

        [Fact]
        public void Exists_ContentDiffers_Updates()
        {
            var path     = WriteText("main.js", "console.log(2);");
            var id       = Guid.NewGuid();
            var svc      = SvcWith(One(id, B64("console.log(1);")));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            result.Updated.ShouldBe(1);
            result.Created.ShouldBe(0);
            svc.Received(1).Update(Arg.Is<Entity>(e => e.LogicalName == "webresource" && e.Id == id));
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void Exists_ContentSame_Skips()
        {
            var path = WriteText("main.js", "console.log(1);");
            var svc  = SvcWith(One(Guid.NewGuid(), B64("console.log(1);")));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            result.Skipped.ShouldBe(1);
            result.Updated.ShouldBe(0);
            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void Exists_OnlyLineEndingsDiffer_Skips()
        {
            // Local CRLF vs remote LF — normalized comparison treats these as identical.
            var path = WriteText("main.js", "a\r\nb\r\n");
            var svc  = SvcWith(One(Guid.NewGuid(), B64("a\nb\n")));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            result.Skipped.ShouldBe(1);
            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void BinaryUnchanged_Skips()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 250 };
            var path  = Path.Combine(_tempDir, "img.png");
            File.WriteAllBytes(path, bytes);
            var svc   = SvcWith(One(Guid.NewGuid(), Convert.ToBase64String(bytes)));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/img.png", path) }, publish: false);

            result.Skipped.ShouldBe(1);
            svc.DidNotReceive().Update(Arg.Any<Entity>());
        }

        [Fact]
        public void ExplicitType_UsedWhenExtensionUnknown()
        {
            var path = WriteText("data.txt", "hello");
            var svc  = SvcWith();

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/data.txt", path, type: 3) }, publish: false);

            result.Created.ShouldBe(1);
            svc.Received(1).Create(Arg.Is<Entity>(e =>
                e.GetAttributeValue<OptionSetValue>("webresourcetype").Value == 3));
        }

        [Fact]
        public void DuplicateName_LastDefinitionWins()
        {
            var folderFile   = WriteText("from-folder.js", "folder");
            var manifestFile = WriteText("from-manifest.js", "manifest");
            var svc          = SvcWith();

            MakeSyncer(svc).Sync(new[]
            {
                Def("pub_/x.js", folderFile),
                Def("pub_/x.js", manifestFile),   // appended later → wins
            }, publish: false);

            svc.Received(1).Create(Arg.Any<Entity>());
            svc.Received(1).Create(Arg.Is<Entity>(e => e.GetAttributeValue<string>("content") == B64("manifest")));
        }

        // ── Warnings ───────────────────────────────────────────────────────────

        [Fact]
        public void MissingFile_Warns_Skips()
        {
            var svc = SvcWith();

            var result = MakeSyncer(svc).Sync(
                new[] { Def("pub_/x.js", Path.Combine(_tempDir, "nope.js")) }, publish: false);

            result.Warnings.ShouldContain(w => w.Contains("not found"));
            result.Created.ShouldBe(0);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        [Fact]
        public void UnknownType_NoExplicitType_Warns_Skips()
        {
            var path = WriteText("data.unknownext", "x");
            var svc  = SvcWith();

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/data.unknownext", path) }, publish: false);

            result.Warnings.ShouldContain(w => w.Contains("type"));
            svc.DidNotReceive().Create(Arg.Any<Entity>());
        }

        // ── Publishing ─────────────────────────────────────────────────────────

        [Fact]
        public void Publish_ExecutesPublishXmlWithChangedIds()
        {
            var path = WriteText("main.js", "x");
            var id   = Guid.NewGuid();
            var svc  = Substitute.For<IOrganizationService>();
            svc.RetrieveMultiple(Arg.Is<QueryExpression>(q => q.EntityName == "webresource"))
               .Returns(new EntityCollection());
            svc.Create(Arg.Any<Entity>()).Returns(id);

            OrganizationRequest? captured = null;
            svc.Execute(Arg.Do<OrganizationRequest>(r => captured = r));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: true);

            result.Published.ShouldBe(1);
            captured.ShouldNotBeNull();
            captured!.RequestName.ShouldBe("PublishXml");
            ((string)captured["ParameterXml"]).ShouldContain($"<webresource>{id}</webresource>");
        }

        [Fact]
        public void NoPublish_DoesNotExecutePublishXml()
        {
            var path = WriteText("main.js", "x");
            var svc  = SvcWith();

            MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: false);

            svc.DidNotReceive().Execute(Arg.Is<OrganizationRequest>(r => r.RequestName == "PublishXml"));
        }

        [Fact]
        public void NothingChanged_DoesNotPublish()
        {
            var path = WriteText("main.js", "same");
            var svc  = SvcWith(One(Guid.NewGuid(), B64("same")));

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, publish: true);

            result.Published.ShouldBe(0);
            svc.DidNotReceive().Execute(Arg.Is<OrganizationRequest>(r => r.RequestName == "PublishXml"));
        }

        // ── Dry run ────────────────────────────────────────────────────────────

        [Fact]
        public void DryRun_CountsButDoesNotWriteOrPublish()
        {
            var path = WriteText("main.js", "x");
            var svc  = SvcWith();

            var result = MakeSyncer(svc).Sync(new[] { Def("pub_/main.js", path) }, dryRun: true, publish: true);

            result.Created.ShouldBe(1);
            svc.DidNotReceive().Create(Arg.Any<Entity>());
            svc.DidNotReceive().Update(Arg.Any<Entity>());
            svc.DidNotReceive().Execute(Arg.Is<OrganizationRequest>(r => r.RequestName == "PublishXml"));
        }

        // ── Solution membership ────────────────────────────────────────────────

        [Fact]
        public void SolutionProvided_Create_ValidatesAndAddsToSolution()
        {
            var path = WriteText("main.js", "x");
            var svc  = SvcWith();
            var sol  = Substitute.For<SolutionService>(svc);

            MakeSyncer(svc, sol).Sync(new[] { Def("pub_/main.js", path) },
                solutionUniqueName: "S", publish: false);

            sol.Received(1).ValidateSolutionExists("S", Arg.Any<bool>());
            sol.Received(1).AddWebResourceToSolution(Arg.Any<Guid>(), "S", Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_Update_AddsToSolution()
        {
            var path = WriteText("main.js", "new");
            var id   = Guid.NewGuid();
            var svc  = SvcWith(One(id, B64("old")));
            var sol  = Substitute.For<SolutionService>(svc);

            MakeSyncer(svc, sol).Sync(new[] { Def("pub_/main.js", path) },
                solutionUniqueName: "S", publish: false);

            sol.Received(1).AddWebResourceToSolution(id, "S", Arg.Any<bool>());
        }

        [Fact]
        public void SolutionProvided_DryRun_DoesNotAddToSolution()
        {
            var path = WriteText("main.js", "x");
            var svc  = SvcWith();
            var sol  = Substitute.For<SolutionService>(svc);

            MakeSyncer(svc, sol).Sync(new[] { Def("pub_/main.js", path) },
                dryRun: true, solutionUniqueName: "S", publish: false);

            sol.DidNotReceive().AddWebResourceToSolution(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>());
        }

        // ── Orphan deletion ────────────────────────────────────────────────────

        [Fact]
        public void DeleteOrphaned_WithoutSolution_Throws()
        {
            var svc = SvcWith();

            Should.Throw<InvalidOperationException>(() =>
                MakeSyncer(svc).Sync(Array.Empty<WebResourceDefinition>(), deleteOrphaned: true));
        }

        [Fact]
        public void DeleteOrphaned_DeletesOrphan_PreservesManaged()
        {
            var path     = WriteText("keep.js", "x");
            var keepId   = Guid.NewGuid();
            var orphanId = Guid.NewGuid();

            var svc = SvcWith(One(keepId, B64("x")));   // "keep" exists, unchanged → managed
            var sol = Substitute.For<SolutionService>(svc);
            sol.GetSolutionWebResources("S", Arg.Any<bool>())
               .Returns(new List<(Guid, string)> { (keepId, "pub_/keep.js"), (orphanId, "pub_/old.js") });

            var result = MakeSyncer(svc, sol).Sync(new[] { Def("pub_/keep.js", path) },
                solutionUniqueName: "S", deleteOrphaned: true, publish: false);

            result.Deleted.ShouldBe(1);
            svc.Received(1).Delete("webresource", orphanId);
            svc.DidNotReceive().Delete("webresource", keepId);
        }

        [Fact]
        public void DeleteOrphaned_DryRun_ReportsButDoesNotDelete()
        {
            var path     = WriteText("keep.js", "x");
            var orphanId = Guid.NewGuid();

            var svc = SvcWith(One(Guid.NewGuid(), B64("x")));
            var sol = Substitute.For<SolutionService>(svc);
            sol.GetSolutionWebResources("S", Arg.Any<bool>())
               .Returns(new List<(Guid, string)> { (orphanId, "pub_/old.js") });

            var result = MakeSyncer(svc, sol).Sync(new[] { Def("pub_/keep.js", path) },
                dryRun: true, solutionUniqueName: "S", deleteOrphaned: true, publish: false);

            result.Deleted.ShouldBe(1);
            svc.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<Guid>());
        }
    }
}
