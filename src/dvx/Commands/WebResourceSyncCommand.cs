using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Models;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging;

namespace dvx.Commands
{
    public static class WebResourceSyncCommand
    {
        public static Command Build(ILoggerFactory loggerFactory)
        {
            var cmd                = new Command("sync",
                "Upsert and publish Dataverse web resources from a folder and/or a manifest file.");
            var env                = CommandOptions.Env();
            var config             = CommandOptions.Config();
            var url                = CommandOptions.Url();
            var clientId           = CommandOptions.ClientId();
            var clientSecret       = CommandOptions.ClientSecret();
            var folder             = CommandOptions.Folder();
            var manifest           = CommandOptions.Manifest();
            var publisherPrefix    = CommandOptions.PublisherPrefix();
            var solutionUniqueName = CommandOptions.SolutionUniqueName();
            var noPublish          = CommandOptions.NoPublish();
            var deleteOrphaned     = CommandOptions.DeleteOrphaned();
            var dryRun             = CommandOptions.DryRun();
            var verbose            = CommandOptions.Verbose();

            cmd.AddOptions(env, config, url, clientId, clientSecret, folder, manifest, publisherPrefix,
                solutionUniqueName, noPublish, deleteOrphaned, dryRun, verbose);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var p           = ctx.ParseResult;
                var envName     = p.GetValueForOption(env);
                var configPath  = p.GetValueForOption(config);
                var cliUrl      = p.GetValueForOption(url);
                var cliClientId = p.GetValueForOption(clientId);
                var cliSecret   = p.GetValueForOption(clientSecret);
                var cliFolder   = p.GetValueForOption(folder);
                var cliManifest = p.GetValueForOption(manifest);
                var cliPrefix   = p.GetValueForOption(publisherPrefix);
                var cliSolution = p.GetValueForOption(solutionUniqueName);
                var noPub       = p.GetValueForOption(noPublish);
                var delOrphaned = p.GetValueForOption(deleteOrphaned);
                var isDryRun    = p.GetValueForOption(dryRun);
                var isVerbose   = p.GetValueForOption(verbose);

                try
                {
                    var appConfig    = ConfigLoader.TryLoad(configPath);
                    var solution     = ConfigLoader.ResolveSolutionUniqueName(appConfig, cliSolution);
                    var folderPath   = ConfigLoader.ResolveWebResourceFolder(appConfig, cliFolder);
                    var manifestPath = ConfigLoader.ResolveWebResourceManifest(appConfig, cliManifest);
                    var publish      = !noPub && (appConfig?.WebResources?.Publish ?? true);

                    if (delOrphaned && solution is null)
                    {
                        Out.Error("--delete-orphaned requires a solution. " +
                                  "Set --solution-unique-name or solutionUniqueName in config.");
                        ctx.ExitCode = 1;
                        return;
                    }

                    if (folderPath is null && manifestPath is null)
                    {
                        Out.Error("No web resource source. Specify --folder or --manifest, " +
                                  "or add webResources.folder to dvx.json.");
                        ctx.ExitCode = 1;
                        return;
                    }

                    if (isVerbose)
                    {
                        Out.Dim($"    folder:          {folderPath ?? "(none)"}");
                        Out.Dim($"    manifest:        {manifestPath ?? "(none)"}");
                        Out.Dim($"    solution:        {solution ?? "(none)"}");
                        Out.Dim($"    publish:         {publish}");
                        Out.Dim($"    delete-orphaned: {delOrphaned}");
                    }

                    var envConfig = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret);
                    using var svc = DataverseClientFactory.Create(envConfig);

                    var desired = new List<WebResourceDefinition>();

                    if (folderPath is not null)
                    {
                        var configured = ConfigLoader.ResolveConfiguredPublisherPrefix(appConfig, cliPrefix);
                        var (prefix, prefixWarning) = PublisherPrefixResolution.Resolve(
                            configured, solution, new SolutionPublisherResolver(svc).GetCustomizationPrefix);
                        if (prefixWarning is not null) Out.Warn(prefixWarning);
                        if (isVerbose) Out.Dim($"    prefix:          {prefix}");

                        var scanned = new WebResourceFolderScanner().Scan(folderPath, prefix, out var skipped);
                        if (isVerbose)
                        {
                            foreach (var d in scanned)
                                Out.Dim($"    + {d.Name}  ({Path.GetFileName(d.LocalPath)})");
                            foreach (var s in skipped)
                                Out.Dim($"    - skipped {s} (unrecognized extension)");
                        }
                        Out.Info($"Discovered {scanned.Count} web resource(s) under {folderPath}.");
                        desired.AddRange(scanned);
                    }

                    if (manifestPath is not null)
                    {
                        var entries = LoadManifest(manifestPath);
                        Out.Info($"Loaded {entries.Count} web resource(s) from manifest {manifestPath}.");
                        desired.AddRange(entries);
                    }

                    if (desired.Count == 0)
                    {
                        Out.Warn("No web resources to process.");
                        return;
                    }

                    if (isDryRun)
                        Out.DryRun("— no changes will be made to Dataverse.");

                    var syncer = new WebResourceSyncer(svc, loggerFactory.CreateLogger<WebResourceSyncer>());
                    var result = syncer.Sync(desired, isDryRun, solution, publish, delOrphaned, isVerbose);

                    Out.WebResourceSummary(
                        result.Created, result.Updated, result.Skipped, result.Deleted, result.Published, isDryRun);
                    foreach (var w in result.Warnings) Out.Warn(w);
                    foreach (var e in result.Errors)   Out.Err(e);

                    if (result.Errors.Count > 0)
                        ctx.ExitCode = 2;
                }
                catch (Exception ex)
                {
                    Out.Error(ex, isVerbose);
                    ctx.ExitCode = 1;
                }
            });

            return cmd;
        }

        /// <summary>
        /// Loads a manifest JSON (array of <see cref="WebResourceManifestEntry"/>) and maps each
        /// entry to a <see cref="WebResourceDefinition"/>. Relative <c>localPath</c> values are
        /// resolved against the manifest file's directory, so the manifest works no matter
        /// where the command is run from.
        /// </summary>
        internal static List<WebResourceDefinition> LoadManifest(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Manifest file not found: {path}");

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var json    = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<WebResourceManifestEntry>>(
                              json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? new List<WebResourceManifestEntry>();

            var defs = new List<WebResourceDefinition>();
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.DataverseName) || string.IsNullOrWhiteSpace(e.LocalPath))
                    continue;

                defs.Add(new WebResourceDefinition
                {
                    Name        = e.DataverseName,
                    LocalPath   = Path.GetFullPath(Path.Combine(baseDir, e.LocalPath)),
                    DisplayName = e.DisplayName,
                    Type        = e.Type,
                });
            }

            return defs;
        }
    }
}
