namespace dvx.Models
{
    /// <summary>
    /// One entry in a web resource manifest JSON file (an array of these). Field names match the
    /// legacy <c>Sync-WebResources.ps1</c> format for familiarity:
    /// <c>{ "dataverseName": "...", "localPath": "...", "displayName": "...", "type": 3 }</c>.
    /// <c>displayName</c> and <c>type</c> are optional (type is inferred from the file extension
    /// when omitted).
    /// </summary>
    public class WebResourceManifestEntry
    {
        public string  DataverseName { get; set; } = string.Empty;
        public string  LocalPath     { get; set; } = string.Empty;
        public string? DisplayName   { get; set; }
        public int?    Type          { get; set; }
    }
}
