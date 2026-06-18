using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class WebResourceFolderScannerTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"dvx-scan-{Guid.NewGuid():N}");

        public WebResourceFolderScannerTests() => Directory.CreateDirectory(_root);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private void Write(string rel, string content = "x")
        {
            var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        [Fact]
        public void DerivesPrefixedForwardSlashNames()
        {
            Write("account/main.js");
            Write("shared/util.js");

            var defs = new WebResourceFolderScanner().Scan(_root, "pub", out _);

            defs.Select(d => d.Name).OrderBy(n => n)
                .ShouldBe(new[] { "pub_/account/main.js", "pub_/shared/util.js" });
        }

        [Fact]
        public void InfersTypeFromExtension()
        {
            Write("a.css");

            var defs = new WebResourceFolderScanner().Scan(_root, "pub", out _);

            defs.ShouldHaveSingleItem().Type.ShouldBe(2);
        }

        [Fact]
        public void SkipsUnknownExtensions_AndReportsThem()
        {
            Write("a.js");
            Write("readme.md");
            Write("data.json");

            var defs = new WebResourceFolderScanner().Scan(_root, "pub", out var skipped);

            defs.Select(d => d.Name).ShouldBe(new[] { "pub_/a.js" });
            skipped.OrderBy(s => s).ShouldBe(new[] { "data.json", "readme.md" });
        }

        [Fact]
        public void IgnoresDotfilesSourceMapsAndBuildDirs()
        {
            Write("app.js");
            Write(".hidden/secret.js");
            Write("node_modules/pkg/index.js");
            Write("bin/out.js");
            Write("app.js.map");

            var defs = new WebResourceFolderScanner().Scan(_root, "pub", out _);

            defs.Select(d => d.Name).ShouldBe(new[] { "pub_/app.js" });
        }

        [Fact]
        public void MissingFolder_Throws()
            => Should.Throw<DirectoryNotFoundException>(
                () => new WebResourceFolderScanner().Scan(Path.Combine(_root, "nope"), "pub", out _));
    }
}
