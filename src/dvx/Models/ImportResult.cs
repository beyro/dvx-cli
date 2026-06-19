namespace dvx.Models
{
    /// <summary>
    /// Result of reading existing steps from Dataverse for adoption: the reconstructed step
    /// definitions plus any warnings about steps that could not be fully represented.
    /// </summary>
    public class ImportResult
    {
        public List<PluginStepDefinition> Definitions { get; } = new();
        public List<string>               Warnings    { get; } = new();

        /// <summary>
        /// Full type names of plugin classes that back a Custom API
        /// </summary>
        public List<string>               CustomApiTypes { get; } = new();
    }
}
