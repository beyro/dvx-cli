using System.CommandLine;
using System.CommandLine.Invocation;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging;

namespace dvx.Commands
{
    public static class RegisterCommand
    {
        public static Command Build(ILoggerFactory loggerFactory)
        {
            var cmd                = new Command("register",
                "Reflect the plugin DLL for [PluginStep] attributes and sync step registrations in Dataverse.");
            var env                = CommandOptions.Env();
            var config             = CommandOptions.Config();
            var url                = CommandOptions.Url();
            var clientId           = CommandOptions.ClientId();
            var clientSecret       = CommandOptions.ClientSecret();
            var project            = CommandOptions.Project();
            var assemblyName       = CommandOptions.AssemblyName();
            var dryRun             = CommandOptions.DryRun();
            var verbose            = CommandOptions.Verbose();
            var solutionUniqueName = CommandOptions.SolutionUniqueName();
            var deleteOrphaned     = CommandOptions.DeleteOrphanedSteps();
            var interactiveAuth    = CommandOptions.InteractiveAuth();

            // --project and --assembly-name are mutually exclusive; both optional — validated at runtime

            cmd.AddOptions(env, config, url, clientId, clientSecret, project, assemblyName,
                dryRun, verbose, solutionUniqueName, deleteOrphaned, interactiveAuth);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var envName     = ctx.ParseResult.GetValueForOption(env);
                var configPath  = ctx.ParseResult.GetValueForOption(config);
                var cliUrl      = ctx.ParseResult.GetValueForOption(url);
                var cliClientId = ctx.ParseResult.GetValueForOption(clientId);
                var cliSecret   = ctx.ParseResult.GetValueForOption(clientSecret);
                var projectPath = ctx.ParseResult.GetValueForOption(project);
                var asmName     = ctx.ParseResult.GetValueForOption(assemblyName);
                var isDryRun    = ctx.ParseResult.GetValueForOption(dryRun);
                var isVerbose   = ctx.ParseResult.GetValueForOption(verbose);
                var cliSolution = ctx.ParseResult.GetValueForOption(solutionUniqueName);
                var delOrphaned = ctx.ParseResult.GetValueForOption(deleteOrphaned);
                var cliInteractive = ctx.ParseResult.GetValueForOption(interactiveAuth);

                if (!string.IsNullOrEmpty(projectPath) && !string.IsNullOrEmpty(asmName))
                {
                    Out.Error("--project and --assembly-name are mutually exclusive. Please provide only one or the other");
                    ctx.ExitCode = 1;
                    return;
                }

                string? tempAssemblyDir = null;
                try
                {
                    var appConfig = ConfigLoader.TryLoad(configPath);
                    var envConfig = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret, cliInteractive);
                    var solution  = ConfigLoader.ResolveSolutionUniqueName(appConfig, cliSolution);
                    using var svc = DataverseClientFactory.Create(envConfig);

                    Guid   assemblyId;
                    string dllPath;

                    // When --assembly-name is not given, resolve the project path from CLI > config > CWD
                    if (string.IsNullOrEmpty(asmName))
                    {
                        projectPath = ConfigLoader.ResolveProject(appConfig, projectPath);
                    }

                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        Out.Step("Building", projectPath!);
                        var build           = new ProjectBuilder().Build(projectPath);
                        dllPath             = build.DllPath;
                        var asmNameFromPath = Path.GetFileNameWithoutExtension(dllPath);
                        assemblyId          = new AssemblyDownloader(svc).FindId(asmNameFromPath);
                    }
                    else
                    {
                        Out.Step("Downloading", $"assembly '{asmName!}' from Dataverse");
                        var downloader = new AssemblyDownloader(svc);
                        (assemblyId, dllPath) = downloader.Download(asmName!);
                        tempAssemblyDir = Path.GetDirectoryName(dllPath);
                    }

                    Out.Step("Discovering", "plugin steps via reflection...");
                    var discovery   = new PluginDiscovery(loggerFactory.CreateLogger<PluginDiscovery>());
                    var definitions = discovery.Discover(dllPath, verbose: isVerbose);
                    Out.Info($"Found {definitions.Count} step definition(s).");

                    if (isDryRun)
                        Out.DryRun("— no changes will be made to Dataverse.");

                    var registrar  = new StepRegistrar(svc, loggerFactory.CreateLogger<StepRegistrar>());
                    var syncResult = registrar.Sync(assemblyId, definitions, isDryRun, solution,
                        deleteOrphaned: delOrphaned, verbose: isVerbose);

                    Out.SyncSummary(syncResult, isDryRun);

                    if (syncResult.Errors.Count > 0)
                        ctx.ExitCode = 2;
                }
                catch (Exception ex)
                {
                    Out.Error(ex, isVerbose);
                    ctx.ExitCode = 1;
                }
                finally
                {
                    if (tempAssemblyDir is not null)
                    {
                        try { Directory.Delete(tempAssemblyDir, recursive: true); }
                        catch { /* best-effort cleanup of the downloaded temp assembly */ }
                    }
                }
            });

            return cmd;
        }
    }
}
