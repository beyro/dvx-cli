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

            var filename = new Option<string?>(
                new[] { "--filename", "-n" },
                "Custom base filename for the reports. If omitted, a timestamped name is used.");

            var types = new Option<string>(
                new[] { "--types", "-t" },
                () => "both",
                "Output types to include: 'csv', 'md', or 'both' (default).")
                .FromAmong("csv", "md", "both");

            cmd.AddOptions(project, config, output, verbose, filename, types);

            cmd.SetHandler(async (InvocationContext ctx) =>
            {
                var projectPath = ctx.ParseResult.GetValueForOption(project);
                var configPath  = ctx.ParseResult.GetValueForOption(config);
                var outDir      = ctx.ParseResult.GetValueForOption(output)!;
                var isVerbose   = ctx.ParseResult.GetValueForOption(verbose);
                var customFilename = ctx.ParseResult.GetValueForOption(filename);
                var reportTypes    = ctx.ParseResult.GetValueForOption(types)!;

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
                    
                    string baseName = !string.IsNullOrEmpty(customFilename) 
                        ? customFilename 
                        : $"plugin-report-{DateTime.Now:yyyyMMdd-HHmmss}";

                    if (!Directory.Exists(outDir))
                    {
                        Directory.CreateDirectory(outDir);
                    }

                    var generatedFiles = new List<string>();

                    if (reportTypes is "md" or "both")
                    {
                        var md = generator.GenerateMarkdown(definitions);
                        var mdFile = Path.Combine(outDir, $"{baseName}.md");
                        await File.WriteAllTextAsync(mdFile, md);
                        generatedFiles.Add(Path.GetFullPath(mdFile));
                    }

                    if (reportTypes is "csv" or "both")
                    {
                        var csv = generator.GenerateCsv(definitions);
                        var csvFile = Path.Combine(outDir, $"{baseName}.csv");
                        await File.WriteAllTextAsync(csvFile, csv);
                        generatedFiles.Add(Path.GetFullPath(csvFile));
                    }

                    Out.Success("Reports generated:", string.Join("\n", generatedFiles));
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
