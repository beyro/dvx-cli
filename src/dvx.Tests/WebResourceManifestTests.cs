using dvx.Commands;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class WebResourceManifestTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dvx-tests-{Guid.NewGuid():N}");

        public WebResourceManifestTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string WriteManifest(string json)
        {
            var path = Path.Combine(_tempDir, "webresources.json");
            File.WriteAllText(path, json);
            return path;
        }

        [Fact]
        public void LoadManifest_RelativeLocalPath_ResolvesAgainstManifestDirectory()
        {
            var path = WriteManifest("""
            [ { "dataverseName": "pub_/account/main.js", "localPath": "./WebResources/account/main.js" } ]
            """);

            var defs = WebResourceSyncCommand.LoadManifest(path);

            defs.Count.ShouldBe(1);
            defs[0].LocalPath.ShouldBe(
                Path.Combine(_tempDir, "WebResources", "account", "main.js"));
        }

        [Fact]
        public void LoadManifest_RootedLocalPath_Unchanged()
        {
            var rooted = Path.Combine(Path.GetTempPath(), "elsewhere", "x.js");
            var path   = WriteManifest($$"""
            [ { "dataverseName": "pub_/x.js", "localPath": {{System.Text.Json.JsonSerializer.Serialize(rooted)}} } ]
            """);

            var defs = WebResourceSyncCommand.LoadManifest(path);

            defs[0].LocalPath.ShouldBe(rooted);
        }

        [Fact]
        public void LoadManifest_SkipsEntriesMissingNameOrPath()
        {
            var path = WriteManifest("""
            [
              { "dataverseName": "pub_/ok.js", "localPath": "./ok.js" },
              { "dataverseName": "", "localPath": "./no-name.js" },
              { "dataverseName": "pub_/no-path.js" }
            ]
            """);

            var defs = WebResourceSyncCommand.LoadManifest(path);

            defs.Count.ShouldBe(1);
            defs[0].Name.ShouldBe("pub_/ok.js");
        }

        [Fact]
        public void LoadManifest_MissingFile_Throws()
        {
            Should.Throw<FileNotFoundException>(() =>
                WebResourceSyncCommand.LoadManifest(Path.Combine(_tempDir, "missing.json")));
        }
    }
}
