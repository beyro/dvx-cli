using System.Text.Json;
using dvx.Config;
using dvx.Models;
using Shouldly;
using Xunit;

namespace dvx.Tests
{
    public class ConfigLoaderTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dvx-tests-{Guid.NewGuid():N}");

        public ConfigLoaderTests() => Directory.CreateDirectory(_tempDir);

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string WriteConfig(string json)
        {
            var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);
            return path;
        }

        // ── TryLoad ────────────────────────────────────────────────────────────

        [Fact]
        public void Load_ValidJson_PopulatesEnvironments()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "cid", "clientSecret": "cs" }
              ]
            }
            """);

            var config = ConfigLoader.TryLoad(path)!;

            config.Environments.Count.ShouldBe(1);
            config.Environments[0].Name.ShouldBe("dev");
            config.Environments[0].Url.ShouldBe("https://dev.crm.dynamics.com");
            config.Environments[0].ClientId.ShouldBe("cid");
            config.Environments[0].ClientSecret.ShouldBe("cs");
        }

        [Fact]
        public void Load_MultipleEnvironments_AllPopulated()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev",  "url": "https://dev.crm.dynamics.com",  "clientId": "c", "clientSecret": "s" },
                { "name": "prod", "url": "https://prod.crm.dynamics.com", "clientId": "c", "clientSecret": "s" }
              ]
            }
            """);

            var config = ConfigLoader.TryLoad(path)!;

            config.Environments.Count.ShouldBe(2);
            config.Environments[1].Name.ShouldBe("prod");
        }

        [Fact]
        public void Load_ExplicitPathMissing_ThrowsWithPathInMessage()
        {
            var missing = Path.Combine(_tempDir, "does-not-exist.json");

            var ex = Should.Throw<InvalidOperationException>(() => ConfigLoader.TryLoad(missing));

            ex.Message.ShouldContain("does-not-exist.json");
            ex.Message.ShouldContain("--config");
        }

        [Fact]
        public void Load_NullJsonBody_ThrowsInvalidOperationException()
        {
            var path = WriteConfig("null");
            Should.Throw<InvalidOperationException>(() => ConfigLoader.TryLoad(path));
        }

        [Fact]
        public void Load_NoExplicitPath_NothingDiscovered_ReturnsNull()
        {
            // Auto-discovery (no explicit path) finding nothing is not an error —
            // commands can still run on CLI options / env vars alone.
            ConfigLoader.TryLoad(null, _tempDir).ShouldBeNull();
        }

        // ── FindConfigFile (upward search) ─────────────────────────────────────

        [Fact]
        public void FindConfigFile_ExplicitPath_ReturnedAsIs_EvenIfMissing()
        {
            var explicitPath = Path.Combine(_tempDir, "custom.json");
            ConfigLoader.FindConfigFile(explicitPath, _tempDir).ShouldBe(explicitPath);
        }

        [Fact]
        public void FindConfigFile_FindsDvxJsonInStartDirectory()
        {
            var configPath = Path.Combine(_tempDir, "dvx.json");
            File.WriteAllText(configPath, """{ "environments": [] }""");

            ConfigLoader.FindConfigFile(null, _tempDir).ShouldBe(configPath);
        }

        [Fact]
        public void FindConfigFile_WalksUpToAncestorDirectory()
        {
            var configPath = Path.Combine(_tempDir, "dvx.json");
            File.WriteAllText(configPath, """{ "environments": [] }""");
            var nested = Directory.CreateDirectory(
                Path.Combine(_tempDir, "src", "WebResources", "account")).FullName;

            ConfigLoader.FindConfigFile(null, nested).ShouldBe(configPath);
        }

        [Fact]
        public void FindConfigFile_NearestAncestorWins()
        {
            File.WriteAllText(Path.Combine(_tempDir, "dvx.json"), """{ "environments": [] }""");
            var mid     = Directory.CreateDirectory(Path.Combine(_tempDir, "sub")).FullName;
            var midPath = Path.Combine(mid, "dvx.json");
            File.WriteAllText(midPath, """{ "environments": [] }""");
            var leaf = Directory.CreateDirectory(Path.Combine(mid, "leaf")).FullName;

            ConfigLoader.FindConfigFile(null, leaf).ShouldBe(midPath);
        }

        [Fact]
        public void FindConfigFile_NothingFound_ReturnsNull()
        {
            // No dvx.json anywhere from the temp dir up to the drive root.
            ConfigLoader.FindConfigFile(null, _tempDir).ShouldBeNull();
        }

        // ── GetEnvironment ─────────────────────────────────────────────────────

        [Fact]
        public void GetEnvironment_ExactName_ReturnsEnvironment()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com", "clientId": "c", "clientSecret": "s" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path)!;

            var env = ConfigLoader.GetEnvironment(config, "dev");

            env.Name.ShouldBe("dev");
        }

        [Fact]
        public void GetEnvironment_CaseInsensitive_ReturnsEnvironment()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "Dev", "url": "https://dev.crm.dynamics.com", "clientId": "c", "clientSecret": "s" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path)!;

            // Name stored as "Dev" — look up as "dev" and "DEV"
            ConfigLoader.GetEnvironment(config, "dev").Name.ShouldBe("Dev");
            ConfigLoader.GetEnvironment(config, "DEV").Name.ShouldBe("Dev");
        }

        [Fact]
        public void GetEnvironment_UnknownName_ThrowsWithAvailableHint()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com", "clientId": "c", "clientSecret": "s" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path)!;

            var ex = Should.Throw<InvalidOperationException>(() => ConfigLoader.GetEnvironment(config, "staging"));
            ex.Message.ShouldContain("Available:");
            ex.Message.ShouldContain("dev");
        }

        // ── publisherPrefix ────────────────────────────────────────────────────

        [Fact]
        public void Load_WithPublisherPrefix_PopulatesPrefix()
        {
            var path = WriteConfig("""{ "environments": [], "publisherPrefix": "solu" }""");
            ConfigLoader.TryLoad(path)!.PublisherPrefix.ShouldBe("solu");
        }

        [Fact]
        public void Load_WithoutPublisherPrefix_PrefixIsNull()
        {
            var path = WriteConfig("""{ "environments": [] }""");
            ConfigLoader.TryLoad(path)!.PublisherPrefix.ShouldBeNull();
        }

        [Fact]
        public void ResolveConfiguredPublisherPrefix_CliOverrideTakesPriority()
        {
            var path   = WriteConfig("""{ "environments": [], "publisherPrefix": "config_prefix" }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveConfiguredPublisherPrefix(config, "cli_prefix").ShouldBe("cli_prefix");
        }

        [Fact]
        public void ResolveConfiguredPublisherPrefix_FallsBackToConfig()
        {
            var path   = WriteConfig("""{ "environments": [], "publisherPrefix": "solu" }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveConfiguredPublisherPrefix(config, null).ShouldBe("solu");
        }

        [Fact]
        public void ResolveConfiguredPublisherPrefix_EmptyCliString_FallsBackToConfig()
        {
            var path   = WriteConfig("""{ "environments": [], "publisherPrefix": "solu" }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveConfiguredPublisherPrefix(config, "").ShouldBe("solu");
            ConfigLoader.ResolveConfiguredPublisherPrefix(config, "   ").ShouldBe("solu");
        }

        [Fact]
        public void ResolveConfiguredPublisherPrefix_NeitherSet_ReturnsNull()
        {
            var path   = WriteConfig("""{ "environments": [] }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveConfiguredPublisherPrefix(config, null).ShouldBeNull();
        }

        // ── solutionUniqueName ─────────────────────────────────────────────────

        [Fact]
        public void Load_WithSolutionUniqueName_PopulatesSolution()
        {
            var path = WriteConfig("""{ "environments": [], "solutionUniqueName": "MySolution" }""");
            ConfigLoader.TryLoad(path)!.SolutionUniqueName.ShouldBe("MySolution");
        }

        [Fact]
        public void Load_WithoutSolutionUniqueName_SolutionIsNull()
        {
            var path = WriteConfig("""{ "environments": [] }""");
            ConfigLoader.TryLoad(path)!.SolutionUniqueName.ShouldBeNull();
        }

        [Fact]
        public void ResolveSolutionUniqueName_CliOverrideTakesPriority()
        {
            var path   = WriteConfig("""{ "environments": [], "solutionUniqueName": "ConfigSolution" }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveSolutionUniqueName(config, "CliSolution").ShouldBe("CliSolution");
        }

        [Fact]
        public void ResolveSolutionUniqueName_FallsBackToConfig()
        {
            var path   = WriteConfig("""{ "environments": [], "solutionUniqueName": "MySolution" }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveSolutionUniqueName(config, null).ShouldBe("MySolution");
        }

        [Fact]
        public void ResolveSolutionUniqueName_NeitherSet_ReturnsNull()
        {
            var path   = WriteConfig("""{ "environments": [] }""");
            var config = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveSolutionUniqueName(config, null).ShouldBeNull();
            ConfigLoader.ResolveSolutionUniqueName(config, "").ShouldBeNull();
            ConfigLoader.ResolveSolutionUniqueName(config, "   ").ShouldBeNull();
        }

        // ── defaultEnvironment ─────────────────────────────────────────────────

        [Fact]
        public void Load_WithDefaultEnvironment_PopulatesDefault()
        {
            var path = WriteConfig("""{ "environments": [], "defaultEnvironment": "dev" }""");
            ConfigLoader.TryLoad(path)!.DefaultEnvironment.ShouldBe("dev");
        }

        [Fact]
        public void Load_WithoutDefaultEnvironment_DefaultIsNull()
        {
            var path = WriteConfig("""{ "environments": [] }""");
            ConfigLoader.TryLoad(path)!.DefaultEnvironment.ShouldBeNull();
        }

        // ── ResolveEnvironmentConfig ───────────────────────────────────────────

        [Fact]
        public void ResolveEnvironmentConfig_AllFromConfig_ReturnsConfigValues()
        {
            var path   = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "c1", "clientSecret": "s1" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path);

            var env = ConfigLoader.ResolveEnvironmentConfig("dev", config, null, null, null);

            env.Url.ShouldBe("https://dev.crm.dynamics.com");
            env.ClientId.ShouldBe("c1");
            env.ClientSecret.ShouldBe("s1");
        }

        [Fact]
        public void ResolveEnvironmentConfig_CliOverridesConfig()
        {
            var path   = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "c1", "clientSecret": "s1" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path);

            var env = ConfigLoader.ResolveEnvironmentConfig(
                "dev", config,
                cliUrl: "https://override.crm.dynamics.com",
                cliClientId: null, cliClientSecret: "cli-secret");

            env.Url.ShouldBe("https://override.crm.dynamics.com");
            env.ClientSecret.ShouldBe("cli-secret");
            env.ClientId.ShouldBe("c1");   // from config
        }

        [Fact]
        public void ResolveEnvironmentConfig_AllFromCli_NoConfigNeeded()
        {
            var env = ConfigLoader.ResolveEnvironmentConfig(
                envName: null, config: null,
                cliUrl: "https://org.crm.dynamics.com",
                cliClientId: "cid",
                cliClientSecret: "secret");

            env.Url.ShouldBe("https://org.crm.dynamics.com");
            env.ClientId.ShouldBe("cid");
            env.ClientSecret.ShouldBe("secret");
        }

        [Fact]
        public void ResolveEnvironmentConfig_UsesDefaultEnvironment_WhenEnvNameOmitted()
        {
            var path   = WriteConfig("""
            {
              "defaultEnvironment": "dev",
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "c1", "clientSecret": "s1" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path);

            var env = ConfigLoader.ResolveEnvironmentConfig(null, config, null, null, null);

            env.Url.ShouldBe("https://dev.crm.dynamics.com");
        }

        [Fact]
        public void ResolveEnvironmentConfig_MissingValues_ThrowsWithFieldNames()
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                ConfigLoader.ResolveEnvironmentConfig(
                    null, null,
                    cliUrl: "https://org.crm.dynamics.com",
                    cliClientId: null, cliClientSecret: null));

            ex.Message.ShouldContain("DVX_CLIENT_ID");
            ex.Message.ShouldContain("DVX_CLIENT_SECRET");
        }

        [Fact]
        public void ResolveEnvironmentConfig_NoEnvAndNoConfig_ThrowsClearMessage()
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                ConfigLoader.ResolveEnvironmentConfig(null, null, null, null, null));

            ex.Message.ShouldContain("--env");
        }

        // ── auth type ──────────────────────────────────────────────────────────

        [Fact]
        public void ResolveEnvironmentConfig_InteractiveFlag_WinsOverConfigAuthType()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "c1", "clientSecret": "s1" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path);

            var env = ConfigLoader.ResolveEnvironmentConfig(
                "dev", config, null, null, null, cliInteractiveAuth: true);

            env.AuthType.ShouldBe(DataverseAuthType.Interactive);
        }

        [Fact]
        public void ResolveEnvironmentConfig_Interactive_RequiresOnlyUrl()
        {
            var env = ConfigLoader.ResolveEnvironmentConfig(
                envName: null, config: null,
                cliUrl: "https://org.crm.dynamics.com",
                cliClientId: null, cliClientSecret: null,
                cliInteractiveAuth: true);

            env.AuthType.ShouldBe(DataverseAuthType.Interactive);
            env.Url.ShouldBe("https://org.crm.dynamics.com");
        }

        [Fact]
        public void ResolveEnvironmentConfig_Interactive_NoUrl_ThrowsNamingUrl()
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                ConfigLoader.ResolveEnvironmentConfig(
                    envName: null, config: null,
                    cliUrl: null, cliClientId: null, cliClientSecret: null,
                    cliInteractiveAuth: true));

            ex.Message.ShouldContain("--url");
        }

        [Fact]
        public void ResolveEnvironmentConfig_Interactive_FromConfigAuthType_NeedsNoSecret()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com", "authType": "interactive" }
              ]
            }
            """);
            var config = ConfigLoader.TryLoad(path);

            var env = ConfigLoader.ResolveEnvironmentConfig("dev", config, null, null, null);

            env.AuthType.ShouldBe(DataverseAuthType.Interactive);
            env.Url.ShouldBe("https://dev.crm.dynamics.com");
        }

        [Fact]
        public void Load_AuthType_ParsesCaseInsensitively()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com", "authType": "Interactive" }
              ]
            }
            """);

            ConfigLoader.TryLoad(path)!.Environments[0].AuthType.ShouldBe(DataverseAuthType.Interactive);
        }

        [Fact]
        public void Load_AuthType_DefaultsToClientSecret_WhenOmitted()
        {
            var path = WriteConfig("""
            {
              "environments": [
                { "name": "dev", "url": "https://dev.crm.dynamics.com",
                  "clientId": "c1", "clientSecret": "s1" }
              ]
            }
            """);

            ConfigLoader.TryLoad(path)!.Environments[0].AuthType.ShouldBe(DataverseAuthType.ClientSecret);
        }

        // ── webResources ───────────────────────────────────────────────────────

        [Fact]
        public void Load_WithWebResources_PopulatesSection()
        {
            var path = WriteConfig("""
            { "environments": [],
              "webResources": { "folder": "./WR", "manifest": "./m.json", "publish": false } }
            """);
            var cfg = ConfigLoader.TryLoad(path)!;

            cfg.WebResources.ShouldNotBeNull();
            cfg.WebResources!.Folder.ShouldBe("./WR");
            cfg.WebResources.Manifest.ShouldBe("./m.json");
            cfg.WebResources.Publish.ShouldBeFalse();
        }

        [Fact]
        public void WebResources_PublishDefaultsTrue_WhenOmitted()
        {
            var path = WriteConfig("""{ "environments": [], "webResources": { "folder": "./WR" } }""");
            ConfigLoader.TryLoad(path)!.WebResources!.Publish.ShouldBeTrue();
        }

        [Fact]
        public void ResolveWebResourceFolder_CliThenConfigThenNull()
        {
            var path = WriteConfig("""{ "environments": [], "webResources": { "folder": "./cfg" } }""");
            var cfg  = ConfigLoader.TryLoad(path);

            // CLI paths are CWD-relative and passed through untouched; config paths
            // resolve against the config file's directory.
            ConfigLoader.ResolveWebResourceFolder(cfg, "./cli").ShouldBe("./cli");
            ConfigLoader.ResolveWebResourceFolder(cfg, null).ShouldBe(Path.Combine(_tempDir, "cfg"));
            ConfigLoader.ResolveWebResourceFolder(null, null).ShouldBeNull();
        }

        [Fact]
        public void ResolveWebResourceManifest_CliThenConfigThenNull()
        {
            var path = WriteConfig("""{ "environments": [], "webResources": { "manifest": "./cfg.json" } }""");
            var cfg  = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveWebResourceManifest(cfg, "./cli.json").ShouldBe("./cli.json");
            ConfigLoader.ResolveWebResourceManifest(cfg, null).ShouldBe(Path.Combine(_tempDir, "cfg.json"));
            ConfigLoader.ResolveWebResourceManifest(null, null).ShouldBeNull();
        }

        // ── Config-relative path resolution ────────────────────────────────────

        [Fact]
        public void TryLoad_SetsConfigDirectory()
        {
            var path = WriteConfig("""{ "environments": [] }""");
            ConfigLoader.TryLoad(path)!.ConfigDirectory.ShouldBe(_tempDir);
        }

        [Fact]
        public void ResolveWebResourceFolder_RootedConfigPath_Unchanged()
        {
            var rooted = Path.Combine(Path.GetTempPath(), "absolute-wr");
            var path   = WriteConfig($$"""
            { "environments": [], "webResources": { "folder": {{JsonSerializer.Serialize(rooted)}} } }
            """);
            var cfg = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveWebResourceFolder(cfg, null).ShouldBe(rooted);
        }

        [Fact]
        public void ResolveProject_ConfigRelativePath_ResolvesAgainstConfigDir()
        {
            var path = WriteConfig("""{ "environments": [], "project": "./src/Plugins.csproj" }""");
            var cfg  = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveProject(cfg, null)
                .ShouldBe(Path.Combine(_tempDir, "src", "Plugins.csproj"));
        }

        [Fact]
        public void ResolveProject_CliPath_PassedThroughUntouched()
        {
            var path = WriteConfig("""{ "environments": [], "project": "./src/Plugins.csproj" }""");
            var cfg  = ConfigLoader.TryLoad(path);

            ConfigLoader.ResolveProject(cfg, "./other.csproj").ShouldBe("./other.csproj");
        }
    }
}
