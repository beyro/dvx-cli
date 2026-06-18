using dvx.Models;
using dvx.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class AttributeWriterTests
    {
        // ── Helpers ──────────────────────────────────────────────────────────────

        private static PluginStepDefinition Def(
            string type, string entity = "account", string message = "Create", int stage = 40) =>
            new() { TypeFullName = type, Entity = entity, Message = message, Stage = stage };

        /// <summary>
        /// Writes <paramref name="sources"/> to a temp dir, runs the writer against a (non-existent)
        /// csproj in that dir, and returns the result plus the resulting file contents (LF-normalized).
        /// </summary>
        private static (AttributeWriteResult Result, Dictionary<string, string> Files) Run(
            Dictionary<string, string> sources,
            IReadOnlyList<PluginStepDefinition> defs,
            bool dryRun = false)
        {
            var dir = Path.Combine(Path.GetTempPath(), "dvx-aw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var (name, content) in sources)
                    File.WriteAllText(Path.Combine(dir, name), content);

                var writer = new AttributeWriter(NullLogger<AttributeWriter>.Instance);
                var result = writer.Write(Path.Combine(dir, "Test.csproj"), defs, dryRun);

                var outFiles = sources.Keys.ToDictionary(
                    n => n,
                    n => File.ReadAllText(Path.Combine(dir, n)).Replace("\r\n", "\n"));
                return (result, outFiles);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private const string SimpleClass =
            "using Microsoft.Xrm.Sdk;\n\n" +
            "namespace My.Plugins\n{\n" +
            "    public class AccountCreate : IPlugin\n    {\n" +
            "        public void Execute(IServiceProvider sp) { }\n    }\n}\n";

        // ── Write: attribute + using insertion ───────────────────────────────────

        [Fact]
        public void AddsAttributeAndUsing_ToSimpleClass()
        {
            var (result, files) = Run(
                new() { ["Account.cs"] = SimpleClass },
                new[] { Def("My.Plugins.AccountCreate", stage: 40) });

            result.Added.ShouldBe(1);
            result.FilesChanged.ShouldHaveSingleItem();

            var text = files["Account.cs"];
            text.ShouldContain("using Microsoft.Xrm.Sdk;\nusing dvx.PluginAttributes;\n");
            text.ShouldContain("    [PluginStep(\"account\", \"Create\", Stage.PostOperation)]\n    public class AccountCreate");
        }

        [Fact]
        public void Idempotent_SkipsExistingEquivalentAttribute()
        {
            var src =
                "using Microsoft.Xrm.Sdk;\nusing dvx.PluginAttributes;\n\n" +
                "namespace My.Plugins\n{\n" +
                "    [PluginStep(\"account\", \"Create\", Stage.PostOperation)]\n" +
                "    public class AccountCreate : IPlugin\n    {\n" +
                "        public void Execute(IServiceProvider sp) { }\n    }\n}\n";

            var (result, files) = Run(
                new() { ["Account.cs"] = src },
                new[] { Def("My.Plugins.AccountCreate", stage: 40) });

            result.Added.ShouldBe(0);
            result.SkippedExisting.ShouldBe(1);
            result.FilesChanged.ShouldBeEmpty();
            files["Account.cs"].ShouldBe(src.Replace("\r\n", "\n"));
        }

        [Fact]
        public void UnmatchedType_Reported_NoChange()
        {
            var (result, files) = Run(
                new() { ["Account.cs"] = SimpleClass },
                new[] { Def("My.Plugins.DoesNotExist") });

            result.Added.ShouldBe(0);
            result.UnmatchedTypes.ShouldContain("My.Plugins.DoesNotExist");
            files["Account.cs"].ShouldBe(SimpleClass);
        }

        [Fact]
        public void FileScopedNamespace_AddsAttribute_NoDuplicateUsing()
        {
            var src =
                "using Microsoft.Xrm.Sdk;\nusing dvx.PluginAttributes;\n" +
                "namespace My.Plugins;\n\n" +
                "public class C : IPlugin\n{\n" +
                "    public void Execute(IServiceProvider sp) { }\n}\n";

            var (result, files) = Run(
                new() { ["C.cs"] = src },
                new[] { Def("My.Plugins.C", message: "Update", stage: 20) });

            result.Added.ShouldBe(1);
            var text = files["C.cs"];
            text.ShouldContain("[PluginStep(\"account\", \"Update\", Stage.PreOperation)]\npublic class C");
            // using already present → must not be duplicated
            (text.Split("using dvx.PluginAttributes;").Length - 1).ShouldBe(1);
        }

        [Fact]
        public void NestedNamespaces_ResolveFullName()
        {
            var src =
                "using Microsoft.Xrm.Sdk;\n" +
                "namespace A\n{\n    namespace B\n    {\n" +
                "        public class C : IPlugin\n        {\n" +
                "            public void Execute(IServiceProvider sp) { }\n        }\n    }\n}\n";

            var (result, files) = Run(
                new() { ["C.cs"] = src },
                new[] { Def("A.B.C") });

            result.Added.ShouldBe(1);
            files["C.cs"].ShouldContain("        [PluginStep(\"account\", \"Create\", Stage.PostOperation)]\n        public class C");
        }

        [Fact]
        public void DryRun_ReportsButDoesNotWrite()
        {
            var (result, files) = Run(
                new() { ["Account.cs"] = SimpleClass },
                new[] { Def("My.Plugins.AccountCreate") },
                dryRun: true);

            result.Added.ShouldBe(1);
            result.FilesChanged.ShouldHaveSingleItem();
            files["Account.cs"].ShouldBe(SimpleClass); // unchanged on disk
        }

        // ── RenderAttribute ──────────────────────────────────────────────────────

        [Fact]
        public void Render_Basic()
        {
            AttributeWriter.RenderAttribute(Def("X", stage: 40))
                .ShouldBe("PluginStep(\"account\", \"Create\", Stage.PostOperation)");
        }

        [Fact]
        public void Render_GlobalMessage_EmptyEntity()
        {
            // Associate / Disassociate carry no entity — rendered with an empty entity literal.
            AttributeWriter.RenderAttribute(Def("X", entity: "", message: "Associate", stage: 40))
                .ShouldBe("PluginStep(\"\", \"Associate\", Stage.PostOperation)");
        }

        [Fact]
        public void Render_FullScalarSet()
        {
            var def = new PluginStepDefinition
            {
                TypeFullName        = "X",
                Entity              = "account",
                Message             = "Update",
                Stage               = 20,
                ExecutionOrder      = 2,
                Mode                = 1,
                Description         = "hi",
                FilteringAttributes = new[] { "name", "statuscode" },
                Configuration       = "cfg",
            };

            AttributeWriter.RenderAttribute(def).ShouldBe(
                "PluginStep(\"account\", \"Update\", Stage.PreOperation, ExecutionOrder = 2, " +
                "Async = true, Description = \"hi\", FilteringAttributes = [\"name\", \"statuscode\"], " +
                "Configuration = \"cfg\")");
        }

        [Fact]
        public void Render_RunAsSystem()
        {
            var def = Def("X");
            def.RunAsUser = Guid.Empty;
            AttributeWriter.RenderAttribute(def).ShouldContain("RunAsSystem = true");
        }

        [Fact]
        public void Render_RunAsSpecificUser()
        {
            var id  = Guid.NewGuid();
            var def = Def("X");
            def.RunAsUser = id;
            AttributeWriter.RenderAttribute(def).ShouldContain($"RunAsUser = \"{id}\"");
        }

        [Fact]
        public void Render_CustomPreImageAlias()
        {
            var def = Def("X");
            def.Images.Add(new ImageDefinition
            {
                ImageType = ImageType.Pre, Alias = "Target", Attributes = new[] { "name" }
            });

            var rendered = AttributeWriter.RenderAttribute(def);
            rendered.ShouldContain("UsePreImage = true");
            rendered.ShouldContain("PreImageAttributes = [\"name\"]");
            rendered.ShouldContain("PreImageAlias = \"Target\"");
        }

        [Fact]
        public void Render_DefaultPostImageAlias_Omitted()
        {
            var def = Def("X", stage: 40);
            def.Images.Add(new ImageDefinition
            {
                ImageType = ImageType.Post, Alias = "PostImage", Attributes = Array.Empty<string>()
            });

            var rendered = AttributeWriter.RenderAttribute(def);
            rendered.ShouldContain("UsePostImage = true");
            rendered.ShouldNotContain("PostImageAlias");
        }
    }
}
