using System.Text.Json;
using dvx.Models;

namespace dvx.Config
{
    public static class ConfigLoader
    {
        /// <summary>
        /// Returns the config file path to use: the explicit path when given, otherwise the
        /// nearest <c>dvx.json</c> found by walking up from <paramref name="startDirectory"/>
        /// (defaults to the current directory) to the filesystem root — like git discovering
        /// <c>.git</c>, so commands work from anywhere inside a project. Returns null if no
        /// file exists.
        /// </summary>
        public static string? FindConfigFile(string? explicitPath = null, string? startDirectory = null)
        {
            if (explicitPath is not null)
                return explicitPath;

            for (var dir = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
                 dir is not null;
                 dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "dvx.json");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// Loads config from the resolved path. Returns null when auto-discovery finds no
        /// config file. Throws when an <paramref name="explicitPath"/> is supplied but the
        /// file does not exist (a typo'd --config should fail loudly, not be silently treated
        /// as "no config"), or when a file is found but cannot be parsed.
        /// </summary>
        public static AppConfig? TryLoad(string? explicitPath = null, string? startDirectory = null)
        {
            if (explicitPath is not null && !File.Exists(explicitPath))
                throw new InvalidOperationException(
                    $"Config file not found: '{explicitPath}'. Check the --config path.");

            var filePath = FindConfigFile(explicitPath, startDirectory);
            if (filePath is null)
                return null;

            var json   = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config is null)
                throw new InvalidOperationException($"Config file at '{filePath}' is empty or invalid.");

            config.ConfigDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            return config;
        }

        /// <summary>
        /// Resolves a path that came from the config file against the config file's directory
        /// (CLI-supplied paths are left alone — they are relative to the CWD by convention).
        /// </summary>
        private static string FromConfigDir(AppConfig config, string path)
            => config.ConfigDirectory is null || Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(config.ConfigDirectory, path));

        /// <summary>
        /// Builds a fully-resolved <see cref="EnvironmentConfig"/> from the priority chain:
        /// CLI args > environment variables > named config entry.
        /// </summary>
        public static EnvironmentConfig ResolveEnvironmentConfig(
            string?    envName,
            AppConfig? config,
            string?    cliUrl,
            string?    cliClientId,
            string?    cliClientSecret)
        {
            // Start from config entry when an environment name is resolvable;
            // fall back to an empty base when all values will come from CLI/env vars.
            EnvironmentConfig base_ = new();
            var resolvedName = envName ?? config?.DefaultEnvironment;
            if (resolvedName is not null)
            {
                if (config is null)
                    throw new InvalidOperationException(
                        $"No config file found. Create dvx.json in the project root (searched " +
                        $"upward from the current directory), or supply credentials via --url / " +
                        $"--client-id / --client-secret (or DVX_* env vars).");

                base_ = GetEnvironment(config, resolvedName);
            }
            else if (cliUrl is null && cliClientId is null && cliClientSecret is null
                     && Environment.GetEnvironmentVariable("DVX_URL") is null
                     && Environment.GetEnvironmentVariable("DVX_CLIENT_ID") is null
                     && Environment.GetEnvironmentVariable("DVX_CLIENT_SECRET") is null)
            {
                throw new InvalidOperationException(
                    "No environment specified. Use --env <name>, set defaultEnvironment in config, " +
                    "or provide --url / --client-id / --client-secret directly.");
            }

            // Layer: environment variables over base
            var url          = Environment.GetEnvironmentVariable("DVX_URL")          ?? base_.Url;
            var clientId     = Environment.GetEnvironmentVariable("DVX_CLIENT_ID")    ?? base_.ClientId;
            var clientSecret = Environment.GetEnvironmentVariable("DVX_CLIENT_SECRET") ?? base_.ClientSecret;

            // Layer: CLI args (highest priority)
            if (!string.IsNullOrWhiteSpace(cliUrl))          url          = cliUrl;
            if (!string.IsNullOrWhiteSpace(cliClientId))     clientId     = cliClientId;
            if (!string.IsNullOrWhiteSpace(cliClientSecret)) clientSecret = cliClientSecret;

            // Validate
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(url))          missing.Add("--url / DVX_URL");
            if (string.IsNullOrWhiteSpace(clientId))     missing.Add("--client-id / DVX_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("--client-secret / DVX_CLIENT_SECRET");

            if (missing.Count > 0)
                throw new InvalidOperationException(
                    "Missing required connection values: " + string.Join(", ", missing));

            return new EnvironmentConfig
            {
                Name         = base_.Name,
                Url          = url,
                ClientId     = clientId,
                ClientSecret = clientSecret,
            };
        }

        /// <summary>
        /// Resolves the explicitly-configured publisher prefix from <c>--publisher-prefix</c> &gt;
        /// <c>publisherPrefix</c> in config. Returns <see langword="null"/> when neither is set —
        /// callers fall back to deriving the prefix from the target solution's publisher
        /// (see <see cref="PublisherPrefixResolution.Resolve"/>).
        /// </summary>
        public static string? ResolveConfiguredPublisherPrefix(AppConfig? config, string? cliOverride)
            => !string.IsNullOrWhiteSpace(cliOverride)            ? cliOverride
             : !string.IsNullOrWhiteSpace(config?.PublisherPrefix) ? config!.PublisherPrefix
             : null;

        public static string? ResolveSolutionUniqueName(AppConfig? config, string? cliOverride)
            => !string.IsNullOrWhiteSpace(cliOverride)              ? cliOverride
             : !string.IsNullOrWhiteSpace(config?.SolutionUniqueName) ? config.SolutionUniqueName
             : null;

        // ── Web resource resolvers ─────────────────────────────────────────────

        public static string? ResolveWebResourceFolder(AppConfig? config, string? cliOverride)
            => !string.IsNullOrWhiteSpace(cliOverride)                  ? cliOverride
             : !string.IsNullOrWhiteSpace(config?.WebResources?.Folder) ? FromConfigDir(config!, config!.WebResources!.Folder!)
             : null;

        public static string? ResolveWebResourceManifest(AppConfig? config, string? cliOverride)
            => !string.IsNullOrWhiteSpace(cliOverride)                    ? cliOverride
             : !string.IsNullOrWhiteSpace(config?.WebResources?.Manifest) ? FromConfigDir(config!, config!.WebResources!.Manifest!)
             : null;

        /// <summary>
        /// Resolves the plugin project path from (in priority order):
        /// <list type="number">
        ///   <item><c>--project</c> CLI option</item>
        ///   <item><c>project</c> field in config</item>
        ///   <item>A single <c>*.csproj</c> file found in the current working directory</item>
        /// </list>
        /// Throws <see cref="InvalidOperationException"/> if none of the sources yield a path,
        /// or if the CWD contains more than one <c>.csproj</c> file (ambiguous).
        /// </summary>
        public static string ResolveProject(AppConfig? config, string? cliOverride)
        {
            if (!string.IsNullOrWhiteSpace(cliOverride))
                return cliOverride;

            if (!string.IsNullOrWhiteSpace(config?.Project))
                return FromConfigDir(config, config.Project!);

            var csproj = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
            return csproj.Length switch
            {
                1 => csproj[0],
                0 => throw new InvalidOperationException(
                        "No project specified. Use --project <path>, add \"project\" to config, " +
                        "or run the command from a directory containing a .csproj file."),
                _ => throw new InvalidOperationException(
                        $"Multiple .csproj files found in the current directory — specify one with --project <path>.")
            };
        }

        public static EnvironmentConfig GetEnvironment(AppConfig config, string name)
        {
            foreach (var env in config.Environments)
                if (string.Equals(env.Name, name, StringComparison.OrdinalIgnoreCase))
                    return env;

            var available = string.Join(", ", config.Environments.Select(e => e.Name));
            throw new InvalidOperationException(
                $"Environment '{name}' not found in config. Available: {available}");
        }
    }
}
