namespace dvx.Models
{
    /// <summary>
    /// Optional <c>webResources</c> section of <c>dvx.json</c> — supplies defaults for
    /// <c>dvx webresource sync</c> so the command can be run with no extra arguments.
    /// </summary>
    public class WebResourceConfig
    {
        /// <summary>Folder to auto-upsert web resources from, recursively.</summary>
        public string? Folder { get; set; }

        /// <summary>
        /// Path to a manifest JSON file (array of
        /// <see cref="WebResourceManifestEntry"/>) listing explicit file → Dataverse-name mappings.
        /// </summary>
        public string? Manifest { get; set; }

        /// <summary>Publish web resources after upsert. Default <see langword="true"/>.</summary>
        public bool Publish { get; set; } = true;
    }
}
