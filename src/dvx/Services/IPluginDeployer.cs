namespace dvx.Services
{
    /// <summary>
    /// A built plugin artifact plus the Dataverse metadata needed to deploy it.
    /// </summary>
    /// <param name="Path">The file to upload — a <c>.nupkg</c> (Package mode) or <c>.dll</c> (Assembly mode).</param>
    /// <param name="AssemblyName">Bare assembly name; the record name in Assembly mode, the DLL stem in Package mode.</param>
    /// <param name="UniqueName"><c>{prefix}_{assemblyName}</c> — the lookup key in Package mode.</param>
    /// <param name="Version">The assembly <c>AssemblyVersion</c> (Assembly mode only).</param>
    public sealed record PluginArtifact(
        string   Path,
        string   AssemblyName,
        string   UniqueName,
        Version? Version);

    /// <summary>
    /// Deploys a built plugin artifact to Dataverse and returns the <c>pluginassembly</c> id
    /// used for downstream plugin step registration.
    /// </summary>
    public interface IPluginDeployer
    {
        Guid Deploy(PluginArtifact artifact, bool verbose = false, bool dryRun = false);
    }
}
