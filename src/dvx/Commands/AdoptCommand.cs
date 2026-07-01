using System.CommandLine;
using System.CommandLine.Invocation;
using System.Xml.Linq;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging;

namespace dvx.Commands
{
    /// <summary>
    /// <c>dvx adopt</c> — bootstraps an existing project onto dvx by reading the steps
    /// already registered on its assembly in Dataverse and writing matching [PluginStep] attributes
    /// into the source. A one-time operation; afterwards use <c>sync</c>/<c>register</c>.
    /// </summary>
    public static class AdoptCommand
    {
        public static Command Build(ILoggerFactory loggerFactory)
        {
            var cmd                = new Command("adopt",
                "Read existing plugin steps from Dataverse and scaffold [PluginStep] attributes onto the source.");
            var env                = CommandOptions.Env();
            var config             = CommandOptions.Config();
            var url                = CommandOptions.Url();
            var clientId           = CommandOptions.ClientId();
            var clientSecret       = CommandOptions.ClientSecret();
            var project            = CommandOptions.Project();
            var assemblyName       = new Option<string?>(
                "--assembly-name",
                "Name of the Dataverse pluginassembly to adopt steps from. " +
                "Defaults to the project's assembly name.");
            var dryRun             = CommandOptions.DryRun();
            var verbose            = CommandOptions.Verbose();
            var interactiveAuth    = CommandOptions.InteractiveAuth();

            cmd.AddOptions(env, config, url, clientId, clientSecret, project, assemblyName,
                dryRun, verbose, interactiveAuth);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var envName       = ctx.ParseResult.GetValueForOption(env);
                var configPath    = ctx.ParseResult.GetValueForOption(config);
                var cliUrl        = ctx.ParseResult.GetValueForOption(url);
                var cliClientId   = ctx.ParseResult.GetValueForOption(clientId);
                var cliSecret     = ctx.ParseResult.GetValueForOption(clientSecret);
                var projectPath   = ctx.ParseResult.GetValueForOption(project);
                var asmName       = ctx.ParseResult.GetValueForOption(assemblyName);
                var isDryRun      = ctx.ParseResult.GetValueForOption(dryRun);
                var isVerbose     = ctx.ParseResult.GetValueForOption(verbose);
                var cliInteractive = ctx.ParseResult.GetValueForOption(interactiveAuth);

                try
                {
                    var appConfig = ConfigLoader.TryLoad(configPath);
                    var envConfig = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret, cliInteractive);
                    var resolvedProject = ConfigLoader.ResolveProject(appConfig, projectPath);
                    using var svc       = DataverseClientFactory.Create(envConfig);

                    var asmResolved = ResolveAssemblyName(resolvedProject, asmName);
                    Out.Step("Resolving", $"assembly '{asmResolved}' in Dataverse");
                    var assemblyId = new AssemblyDownloader(svc).FindId(asmResolved);

                    Out.Step("Reading", "existing steps from Dataverse...");
                    var importer     = new StepImporter(svc, loggerFactory.CreateLogger<StepImporter>());
                    var importResult = importer.Import(assemblyId, isVerbose);
                    Out.Info($"Found {importResult.Definitions.Count} step(s) to adopt.");
                    if (importResult.CustomApiTypes.Count > 0)
                        Out.Info($"Skipping {importResult.CustomApiTypes.Count} Custom API implementation(s) — will mark [CustomApi].");
                    foreach (var w in importResult.Warnings) Out.Warn(w);

                    if (isDryRun)
                        Out.DryRun("— no files will be modified.");

                    Out.Step("Writing", $"[PluginStep] attributes into {Path.GetFileName(resolvedProject)}");
                    var writer      = new AttributeWriter(loggerFactory.CreateLogger<AttributeWriter>());
                    var writeResult = writer.Write(
                        resolvedProject, importResult.Definitions, isDryRun, isVerbose, importResult.CustomApiTypes);

                    foreach (var line in writeResult.Planned)        Out.SubStep(line);
                    foreach (var t in writeResult.UnmatchedTypes)    Out.Warn($"No source class found for '{t}' — skipped.");
                    foreach (var w in writeResult.Warnings)          Out.Warn(w);

                    Out.Info("");
                    Out.Success(isDryRun ? "Would adopt:" : "Adopted:",
                        $"{writeResult.Added} attribute(s) across {writeResult.FilesChanged.Count} file(s); " +
                        $"{writeResult.SkippedExisting} already present; " +
                        $"{writeResult.CustomApisMarked} Custom API class(es) marked [CustomApi].");

                    if (!isDryRun && writeResult.Added > 0)
                        Out.Info("Next: review the changes (git diff), then run 'dvx sync' " +
                                 "to bring Dataverse under attribute control.");
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
        /// Resolves the Dataverse pluginassembly name: the explicit override if given, else the
        /// project's &lt;AssemblyName&gt; from the csproj, else the csproj file name.
        /// </summary>
        internal static string ResolveAssemblyName(string projectPath, string? cliOverride)
        {
            if (!string.IsNullOrWhiteSpace(cliOverride))
                return cliOverride;

            try
            {
                var asmName = XDocument.Load(projectPath)
                    .Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "AssemblyName")?.Value;
                if (!string.IsNullOrWhiteSpace(asmName))
                    return asmName.Trim();
            }
            catch
            {
                // Not fatal — fall back to the file name below.
            }

            return Path.GetFileNameWithoutExtension(projectPath);
        }
    }
}
