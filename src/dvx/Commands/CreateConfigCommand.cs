using System.CommandLine;
using System.CommandLine.Invocation;
using dvx.Output;

namespace dvx.Commands
{
    public static class CreateConfigCommand
    {
        private const string Template = """
            {
              "defaultEnvironment": "dev",
              "publisherPrefix": "yourprefix",
              "solutionUniqueName": "",
              "environments": [
                {
                  "name": "dev",
                  "url": "https://your-dev-org.crm4.dynamics.com",
                  "clientId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
                  "clientSecret": "your-secret"
                }
              ],
              "webResources": {
                "folder": "./WebResources",
                "publish": true
              }
            }
            """;

        public static Command Build()
        {
            var cmd      = new Command("create", "Create a template dvx.json configuration file.");
            var location  = new Option<string?>(
                "--location",
                "Directory to create dvx.json in. Defaults to the current directory.");
            var overwrite = new Option<bool>(
                "--overwrite",
                "Overwrite dvx.json if it already exists.");

            cmd.AddOption(location);
            cmd.AddOption(overwrite);

            cmd.SetHandler((InvocationContext ctx) =>
            {
                var dir         = ctx.ParseResult.GetValueForOption(location);
                var canOverwrite = ctx.ParseResult.GetValueForOption(overwrite);
                var outDir      = string.IsNullOrWhiteSpace(dir) ? Directory.GetCurrentDirectory() : dir;
                var filePath    = Path.Combine(outDir, "dvx.json");

                try
                {
                    if (File.Exists(filePath) && !canOverwrite)
                    {
                        Out.Error($"'{filePath}' already exists. Pass --overwrite to replace it.");
                        ctx.ExitCode = 1;
                        return;
                    }

                    Directory.CreateDirectory(outDir);
                    File.WriteAllText(filePath, Template);
                    Out.Success("Created", filePath);
                }
                catch (Exception ex)
                {
                    Out.Error(ex, verbose: false);
                    ctx.ExitCode = 1;
                }
            });

            return cmd;
        }
    }
}
