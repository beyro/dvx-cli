namespace dvx.Models
{
    /// <summary>
    /// A resolved web resource to upsert into Dataverse: its <see cref="Name"/> (the Dataverse
    /// <c>webresource.name</c>), the local file to read content from, an optional display name,
    /// and an optional explicit <see cref="Type"/> (<c>webresourcetype</c> option-set value).
    /// When <see cref="Type"/> is null the type is inferred from the file extension.
    /// </summary>
    public class WebResourceDefinition
    {
        public string  Name        { get; set; } = string.Empty;
        public string  LocalPath   { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public int?    Type        { get; set; }
    }
}
