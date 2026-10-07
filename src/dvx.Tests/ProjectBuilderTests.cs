using dvx.Services;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class ProjectBuilderTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dvx-pb-{Guid.NewGuid():N}");
        private readonly string _projectPath;

        public ProjectBuilderTests()
        {
            Directory.CreateDirectory(Path.Combine(_tempDir, "bin", "Release"));
            _projectPath = Path.Combine(_tempDir, "MyPlugin.csproj");
            File.WriteAllText(_projectPath, "<Project />");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string ReleaseDir => Path.Combine(_tempDir, "bin", "Release");

        // Skips the real 'dotnet build' so tests exercise only artifact discovery.
        private sealed class NoBuildProjectBuilder : ProjectBuilder
        {
            protected override void RunBuild(string projectPath) { }
        }

        [Fact]
        public void BuildAssembly_DllOnly_ReturnsArtifactWithoutRequiringNupkg()
        {
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.dll"), "dll");

            var result = new NoBuildProjectBuilder().BuildAssembly(_projectPath);

            result.DllPath.ShouldBe(Path.Combine(ReleaseDir, "MyPlugin.dll"));
            result.NupkgPath.ShouldBeNull();
        }

        [Fact]
        public void BuildAssembly_WithNupkg_ReturnsBoth()
        {
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.1.0.0.nupkg"), "pkg");
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.dll"), "dll");

            var result = new NoBuildProjectBuilder().BuildAssembly(_projectPath);

            result.NupkgPath.ShouldBe(Path.Combine(ReleaseDir, "MyPlugin.1.0.0.nupkg"));
            result.DllPath.ShouldBe(Path.Combine(ReleaseDir, "MyPlugin.dll"));
        }

        [Fact]
        public void Build_PackageProject_ReturnsBothArtifacts()
        {
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.1.0.0.nupkg"), "pkg");
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.dll"), "dll");

            var result = new NoBuildProjectBuilder().Build(_projectPath);

            result.NupkgPath.ShouldBe(Path.Combine(ReleaseDir, "MyPlugin.1.0.0.nupkg"));
            result.DllPath.ShouldBe(Path.Combine(ReleaseDir, "MyPlugin.dll"));
        }

        [Fact]
        public void Build_PackageProject_MissingNupkg_Throws()
        {
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.dll"), "dll");

            Should.Throw<InvalidOperationException>(() => new NoBuildProjectBuilder().Build(_projectPath));
        }

        [Fact]
        public void BuildAssembly_MissingDll_Throws()
        {
            File.WriteAllText(Path.Combine(ReleaseDir, "MyPlugin.1.0.0.nupkg"), "pkg");

            Should.Throw<InvalidOperationException>(() => new NoBuildProjectBuilder().BuildAssembly(_projectPath));
        }
    }
}
