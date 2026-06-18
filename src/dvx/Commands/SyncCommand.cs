using System.CommandLine;
using System.CommandLine.Invocation;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging;

namespace dvx.Commands
{
    public static class SyncCommand
    {
        public static Command Build(ILoggerFactory loggerFactory)
        {
            var cmd                = new Command("sync", "Build, deploy, and register plugin steps in one operation.");
            var env                = CommandOptions.Env();
            var config             = CommandOptions.Config();
            var url                = CommandOptions.Url();
            var clientId           = CommandOptions.ClientId();
            var clientSecret       = CommandOptions.ClientSecret();
            var project            = CommandOptions.Project();
            var publisherPrefix    = CommandOptions.PublisherPrefix();
            var dryRun             = CommandOptions.DryRun();
            var verbose            = CommandOptions.Verbose();
            var solutionUniqueName = CommandOptions.SolutionUniqueName();

            cmd.AddOptions(env, config, url, clientId, clientSecret, project, publisherPrefix,
                dryRun, verbose, solutionUniqueName);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var envName     = ctx.ParseResult.GetValueForOption(env);
                var configPath  = ctx.ParseResult.GetValueForOption(config);
                var cliUrl      = ctx.ParseResult.GetValueForOption(url);
                var cliClientId = ctx.ParseResult.GetValueForOption(clientId);
                var cliSecret   = ctx.ParseResult.GetValueForOption(clientSecret);
                var projectPath = ctx.ParseResult.GetValueForOption(project)!;
                var cliPrefix   = ctx.ParseResult.GetValueForOption(publisherPrefix);
                var isDryRun    = ctx.ParseResult.GetValueForOption(dryRun);
                var isVerbose   = ctx.ParseResult.GetValueForOption(verbose);
                var cliSolution = ctx.ParseResult.GetValueForOption(solutionUniqueName);

                try
                {
                    var appConfig       = ConfigLoader.TryLoad(configPath);
                    var envConfig       = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret);
                    var configured      = ConfigLoader.ResolveConfiguredPublisherPrefix(appConfig, cliPrefix);
                    var solution        = ConfigLoader.ResolveSolutionUniqueName(appConfig, cliSolution);
                    var resolvedProject = ConfigLoader.ResolveProject(appConfig, projectPath);
                    using var svc       = DataverseClientFactory.Create(envConfig);

                    var (prefix, prefixWarning) = PublisherPrefixResolution.Resolve(
                        configured, solution, new SolutionPublisherResolver(svc).GetCustomizationPrefix);
                    if (prefixWarning is not null) Out.Warn(prefixWarning);

                    // ── Build ───────────────────────────────────────────────
                    Out.Step("Building", resolvedProject);
                    var build        = new ProjectBuilder().Build(resolvedProject);
                    var assemblyName = Path.GetFileNameWithoutExtension(build.DllPath);
                    Out.Success("Built", Path.GetFileName(build.NupkgPath));

                    // ── Deploy ──────────────────────────────────────────────
                    Out.Step("Deploying", $"to {envConfig.Url}");
                    var uniqueName = $"{prefix}_{assemblyName}";
                    var deployer   = new PackageDeployer(svc);
                    var assemblyId = deployer.Deploy(build.NupkgPath, uniqueName, isVerbose, isDryRun);
                    Out.Success(isDryRun ? "Resolved assembly (upload skipped — dry run)." : "Deployed.",
                        $"Assembly ID: {assemblyId}");

                    // ── Register ────────────────────────────────────────────
                    Out.Step("Discovering", "plugin steps via reflection...");
                    var discovery   = new PluginDiscovery(loggerFactory.CreateLogger<PluginDiscovery>());
                    var definitions = discovery.Discover(build.DllPath, isVerbose);
                    Out.Info($"Found {definitions.Count} step definition(s).");

                    if (isDryRun)
                        Out.DryRun("— no step changes will be made.");

                    var registrar  = new StepRegistrar(svc, loggerFactory.CreateLogger<StepRegistrar>());
                    var syncResult = registrar.Sync(assemblyId, definitions, isDryRun, solution, isVerbose);

                    Out.SyncSummary(syncResult, isDryRun);

                    if (syncResult.Errors.Count > 0)
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
    }
}
