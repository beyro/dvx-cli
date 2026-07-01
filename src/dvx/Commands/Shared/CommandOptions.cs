using System.CommandLine;

namespace dvx.Commands.Shared
{
    public static class CommandOptions
    {
        public static Option<string?> Env() => new Option<string?>(
            "--env",
            "Named environment from config file. Optional if defaultEnvironment is set in config " +
            "or if --url / --client-id / --client-secret are provided.");

        public static Option<string?> Config() => new Option<string?>(
            "--config",
            "Path to config file. Defaults to the nearest dvx.json, searched upward " +
            "from the current directory (like git).");

        public static Option<string?> Url() => new Option<string?>(
            "--url",
            "Dataverse environment URL (e.g. https://org.crm.dynamics.com). " +
            "Overrides the url from the config file. Env var: DVX_URL.");

        public static Option<string?> ClientId() => new Option<string?>(
            "--client-id",
            "Service principal client (application) ID. " +
            "Overrides config. Env var: DVX_CLIENT_ID.");

        public static Option<string?> ClientSecret() => new Option<string?>(
            "--client-secret",
            "Service principal client secret. " +
            "Overrides config. Env var: DVX_CLIENT_SECRET.");

        public static Option<bool> InteractiveAuth() => new Option<bool>(
            "--interactive-auth",
            "Sign in interactively via the browser instead of a client secret (local dev). " +
            "Token is cached securely. Equivalent to authType=interactive in config.");

        public static Option<string?> Project() => new Option<string?>(
            "--project",
            "Path to the plugin .csproj file. The tool will run 'dotnet build' to produce the .nupkg and .dll. " +
            "If omitted, falls back to the 'project' field in config, then a single .csproj in the current directory.");

        public static Option<string?> PublisherPrefix() => new Option<string?>(
            "--publisher-prefix",
            "Dataverse publisher customization prefix (e.g. 'solu'). Required for package " +
            "deployment. If omitted, falls back to publisherPrefix in config.");

        public static Option<string?> AssemblyName() => new Option<string?>(
            "--assembly-name",
            "Name of the pluginassembly record already in Dataverse. " +
            "The DLL will be downloaded from its content field for reflection.");

        public static Option<string?> SolutionUniqueName() => new Option<string?>(
            "--solution-unique-name",
            "Unique name of the Dataverse solution to add deployed components " +
            "(plugin steps / web resources) to. Falls back to solutionUniqueName in config.");

        public static Option<string?> Folder() => new Option<string?>(
            "--folder",
            "Folder to auto-upsert web resources from, recursively. Each file's Dataverse name is " +
            "derived as {prefix}_/{relativePath}. Falls back to webResources.folder in config.");

        public static Option<string?> Manifest() => new Option<string?>(
            "--manifest",
            "Path to a web resource manifest JSON (array of " +
            "{ dataverseName, localPath, displayName, type }). Falls back to webResources.manifest in config.");

        public static Option<bool> NoPublish() => new Option<bool>(
            "--no-publish",
            "Skip publishing web resources after upsert.");

        public static Option<bool> DeleteOrphaned() => new Option<bool>(
            "--delete-orphaned",
            "Delete web resources that are in the target solution but not in the folder/manifest. " +
            "Requires a solution. Destructive — run with --dry-run first.");

        public static Option<bool> DeleteOrphanedSteps() => new Option<bool>(
            "--delete-orphaned",
            "Delete plugin steps registered in Dataverse but no longer present in code. " +
            "Steps backing Custom APIs and Custom Actions are never removed. " +
            "Destructive — run with --dry-run first.");

        public static Option<bool> DryRun() => new Option<bool>(
            "--dry-run",
            "Print what would happen without making any changes to Dataverse.");

        public static Option<bool> Verbose() => new Option<bool>(
            "--verbose",
            "Enable verbose/debug logging.");
    }
}
