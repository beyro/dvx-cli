using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Models;
using dvx.Output;
using dvx.Services;

namespace dvx.Commands
{
    public static class DeployCommand
    {
        public static Command Build()
        {
            var cmd             = new Command("deploy", "Build and push the plugin package to Dataverse.");
            var env             = CommandOptions.Env();
            var config          = CommandOptions.Config();
            var url             = CommandOptions.Url();
            var clientId        = CommandOptions.ClientId();
            var clientSecret    = CommandOptions.ClientSecret();
            var project         = CommandOptions.Project();
            var publisherPrefix = CommandOptions.PublisherPrefix();
            var solutionUniqueName = CommandOptions.SolutionUniqueName();
            var pluginBuildMode = CommandOptions.PluginBuildMode();
            var interactiveAuth = CommandOptions.InteractiveAuth();
            var dryRun          = CommandOptions.DryRun();
            var verbose         = CommandOptions.Verbose();

            cmd.AddOptions(env, config, url, clientId, clientSecret, project, publisherPrefix,
                solutionUniqueName, pluginBuildMode, interactiveAuth, dryRun, verbose);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var envName      = ctx.ParseResult.GetValueForOption(env);
                var configPath   = ctx.ParseResult.GetValueForOption(config);
                var cliUrl       = ctx.ParseResult.GetValueForOption(url);
                var cliClientId  = ctx.ParseResult.GetValueForOption(clientId);
                var cliSecret    = ctx.ParseResult.GetValueForOption(clientSecret);
                var projectPath  = ctx.ParseResult.GetValueForOption(project)!;
                var pubPrefix    = ctx.ParseResult.GetValueForOption(publisherPrefix);
                var cliSolution  = ctx.ParseResult.GetValueForOption(solutionUniqueName);
                var cliMode      = ctx.ParseResult.GetValueForOption(pluginBuildMode);
                var cliInteractive = ctx.ParseResult.GetValueForOption(interactiveAuth);
                var isDryRun     = ctx.ParseResult.GetValueForOption(dryRun);
                var isVerbose    = ctx.ParseResult.GetValueForOption(verbose);

                try
                {
                    var appConfig   = ConfigLoader.TryLoad(configPath);
                    var envConfig   = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret, cliInteractive);
                    var configured  = ConfigLoader.ResolveConfiguredPublisherPrefix(appConfig, pubPrefix);
                    var solution    = ConfigLoader.ResolveSolutionUniqueName(appConfig, cliSolution);
                    var resolvedProject = ConfigLoader.ResolveProject(appConfig, projectPath);
                    var mode        = ConfigLoader.ResolvePluginBuildMode(appConfig, cliMode);
                    using var svc   = DataverseClientFactory.Create(envConfig);

                    var (prefix, prefixWarning) = PublisherPrefixResolution.Resolve(
                        configured, solution, new SolutionPublisherResolver(svc).GetCustomizationPrefix);
                    if (prefixWarning is not null) Out.Warn(prefixWarning);

                    Out.Step("Building", resolvedProject);
                    var build = new ProjectBuilder().BuildAssembly(resolvedProject);
                    var plan  = PluginDeploymentPlan.Resolve(mode, build);
                    if (plan.Warning is not null) Out.Warn(plan.Warning);

                    var assemblyName = Path.GetFileNameWithoutExtension(build.DllPath);
                    var uniqueName   = $"{prefix}_{assemblyName}";
                    var version      = AssemblyName.GetAssemblyName(build.DllPath).Version;
                    var artifact     = new PluginArtifact(plan.ArtifactPath, assemblyName, uniqueName, version);
                    Out.Success("Built", Path.GetFileName(plan.ArtifactPath));

                    Out.Step("Deploying", $"to {envConfig.Url}");
                    IPluginDeployer deployer = plan.Mode == PluginBuildMode.Package
                        ? new PackageDeployer(svc)
                        : new AssemblyDeployer(svc);
                    var assemblyId = deployer.Deploy(artifact, isVerbose, isDryRun);

                    if (plan.Mode == PluginBuildMode.Assembly && solution is not null && !isDryRun && assemblyId != Guid.Empty)
                        new SolutionService(svc).AddAssemblyToSolution(assemblyId, solution, isVerbose);

                    Out.Success(isDryRun ? "Resolved assembly (upload skipped — dry run)." : "Deployed.",
                        $"Assembly ID: {assemblyId}");
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
