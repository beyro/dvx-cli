namespace dvx.Models
{
    /// <summary>
    /// Selects which Dataverse record the plugin build artifact is deployed to:
    /// <see cref="Package"/> uploads the <c>.nupkg</c> to a <c>pluginpackage</c> record,
    /// <see cref="Assembly"/> uploads the <c>.dll</c> directly to a <c>pluginassembly</c> record.
    /// </summary>
    public enum PluginBuildMode
    {
        Package,
        Assembly,
    }
}
