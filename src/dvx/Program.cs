using System.CommandLine;
using dvx.Commands;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

var root = new RootCommand("dvx — deploy Dataverse / Power Platform code artifacts");

var plugin = new Command("plugin", "Deploy and register Dataverse plugin assemblies.");
plugin.AddCommand(DeployCommand.Build());
plugin.AddCommand(RegisterCommand.Build(loggerFactory));
plugin.AddCommand(SyncCommand.Build(loggerFactory));
plugin.AddCommand(AdoptCommand.Build(loggerFactory));
root.AddCommand(plugin);

var webresource = new Command("webresource", "Deploy and publish Dataverse web resources.");
webresource.AddAlias("wr");
webresource.AddCommand(WebResourceSyncCommand.Build(loggerFactory));
root.AddCommand(webresource);

var config = new Command("config", "Manage dvx configuration.");
config.AddCommand(CreateConfigCommand.Build());
root.AddCommand(config);

return await root.InvokeAsync(args);
