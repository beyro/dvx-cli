using System.Text.Json.Serialization;

namespace dvx.Models
{
    public class AppConfig
    {
        /// <summary>
        /// Directory containing the loaded config file. Set by the loader, not part of the
        /// JSON schema. Relative paths inside the config (<see cref="Project"/>,
        /// <c>webResources.folder</c>/<c>manifest</c>) resolve against this rather than the
        /// current working directory, so commands behave the same from any subdirectory.
        /// </summary>
        [JsonIgnore]
        public string? ConfigDirectory { get; set; }

        public List<EnvironmentConfig> Environments { get; set; } = new();

        /// <summary>
        /// Dataverse publisher customization prefix (e.g. <c>"solu"</c>).
        /// Required for plugin package deployment; can be overridden per-command with
        /// <c>--publisher-prefix</c>.
        /// </summary>
        public string? PublisherPrefix { get; set; }

        /// <summary>
        /// Unique name of the Dataverse solution to add registered plugin steps to.
        /// Can be overridden per-command with <c>--solution-unique-name</c>.
        /// </summary>
        public string? SolutionUniqueName { get; set; }

        /// <summary>
        /// Name of the environment to use when <c>--env</c> is not specified on the command line.
        /// </summary>
        public string? DefaultEnvironment { get; set; }

        /// <summary>
        /// Path to the plugin <c>.csproj</c> file.
        /// Used when <c>--project</c> is not specified on the command line.
        /// </summary>
        public string? Project { get; set; }

        /// <summary>
        /// Optional defaults for <c>dvx webresource sync</c> (source folder/manifest, name prefix,
        /// publish behaviour).
        /// </summary>
        public WebResourceConfig? WebResources { get; set; }
    }
}
