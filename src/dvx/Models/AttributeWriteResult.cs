namespace dvx.Models
{
    /// <summary>
    /// Outcome of writing [PluginStep] attributes into source files during adoption.
    /// </summary>
    public class AttributeWriteResult
    {
        /// <summary>Number of [PluginStep] attributes added.</summary>
        public int Added { get; set; }

        /// <summary>Number of steps skipped because an equivalent attribute already existed.</summary>
        public int SkippedExisting { get; set; }

        /// <summary>Source files that were (or would be) modified, relative to the project dir.</summary>
        public List<string> FilesChanged { get; } = new();

        /// <summary>Dataverse type names with no matching class in the project.</summary>
        public List<string> UnmatchedTypes { get; } = new();

        public List<string> Warnings { get; } = new();

        /// <summary>Human-readable "would add X to Y" lines, populated for both real and dry runs.</summary>
        public List<string> Planned { get; } = new();
    }
}
