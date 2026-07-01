using System.CommandLine;
using System.CommandLine.Invocation;
using dvx.Commands.Shared;
using dvx.Config;
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
            var interactiveAuth = CommandOptions.InteractiveAuth();
            var verbose         = CommandOptions.Verbose();

            cmd.AddOptions(env, config, url, clientId, clientSecret, project, publisherPrefix,
                solutionUniqueName, interactiveAuth, verbose);

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
                var cliInteractive = ctx.ParseResult.GetValueForOption(interactiveAuth);
                var isVerbose    = ctx.ParseResult.GetValueForOption(verbose);

                try
                {
                    var appConfig   = ConfigLoader.TryLoad(configPath);
                    var envConfig   = ConfigLoader.ResolveEnvironmentConfig(
                        envName, appConfig, cliUrl, cliClientId, cliSecret, cliInteractive);
                    var configured  = ConfigLoader.ResolveConfiguredPublisherPrefix(appConfig, pubPrefix);
                    var solution    = ConfigLoader.ResolveSolutionUniqueName(appConfig, cliSolution);
                    var resolvedProject = ConfigLoader.ResolveProject(appConfig, projectPath);
                    using var svc   = DataverseClientFactory.Create(envConfig);

                    var (prefix, prefixWarning) = PublisherPrefixResolution.Resolve(
                        configured, solution, new SolutionPublisherResolver(svc).GetCustomizationPrefix);
                    if (prefixWarning is not null) Out.Warn(prefixWarning);

                    Out.Step("Building", resolvedProject);
                    var build        = new ProjectBuilder().Build(resolvedProject);
                    var assemblyName = Path.GetFileNameWithoutExtension(build.DllPath);
                    Out.Success("Built", Path.GetFileName(build.NupkgPath));

                    Out.Step("Deploying", $"to {envConfig.Url}");
                    var uniqueName = $"{prefix}_{assemblyName}";
                    var deployer   = new PackageDeployer(svc);
                    var assemblyId = deployer.Deploy(build.NupkgPath, uniqueName, isVerbose);

                    Out.Success("Deployed.", $"Assembly ID: {assemblyId}");
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
