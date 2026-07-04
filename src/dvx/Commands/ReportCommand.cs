using System.CommandLine;
using System.CommandLine.Invocation;
using dvx.Commands.Shared;
using dvx.Config;
using dvx.Output;
using dvx.Services;
using Microsoft.Extensions.Logging;

namespace dvx.Commands
{
    /// <summary>
    /// <c>dvx plugin report</c> — scans the local project for [PluginStep] attributes,
    /// discovers registrations via reflection on the built assembly, and generates a
    /// registration report in Markdown and CSV formats.
    /// </summary>
    public static class ReportCommand
    {
        public static Command Build(ILoggerFactory loggerFactory)
        {
            var cmd = new Command("report", "Generate a report of plugin registrations in the project.");

            var project = CommandOptions.Project();
            var config  = CommandOptions.Config();
            var output  = new Option<string>(
                new[] { "--out", "-o" },
                () => "reports",
                "Output directory for the generated reports.");
            var verbose = CommandOptions.Verbose();

            cmd.AddOptions(project, config, output, verbose);

            cmd.SetHandler(async (InvocationContext ctx) =>
            {
                var projectPath = ctx.ParseResult.GetValueForOption(project);
                var configPath  = ctx.ParseResult.GetValueForOption(config);
                var outDir      = ctx.ParseResult.GetValueForOption(output)!;
                var isVerbose   = ctx.ParseResult.GetValueForOption(verbose);

                try
                {
                    var appConfig       = ConfigLoader.TryLoad(configPath);
                    var resolvedProject = ConfigLoader.ResolveProject(appConfig, projectPath);

                    Out.Step("Building", $"project {Path.GetFileName(resolvedProject)}...");
                    var builder     = new ProjectBuilder();
                    var buildResult = builder.Build(resolvedProject);

                    Out.Step("Discovering", "plugin registrations from assembly...");
                    var discovery   = new PluginDiscovery(loggerFactory.CreateLogger<PluginDiscovery>());
                    var definitions = discovery.Discover(buildResult.DllPath, isVerbose);

                    Out.Info($"Found {definitions.Count} registration(s).");

                    Out.Step("Generating", "reports...");
                    var generator = new ReportGenerator();
                    var md        = generator.GenerateMarkdown(definitions);
                    var csv       = generator.GenerateCsv(definitions);

                    if (!Directory.Exists(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                    }

                    var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    var mdFile    = Path.Combine(outDir, $"plugin-report-{timestamp}.md");
                    var csvFile   = Path.Combine(outDir, $"plugin-report-{timestamp}.csv");

                    await File.WriteAllTextAsync(mdFile, md);
                    await File.WriteAllTextAsync(csvFile, csv);

                    Out.Success("Reports generated:", $"{Path.GetFullPath(mdFile)}\n{Path.GetFullPath(csvFile)}");
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
