namespace dvx.Models
{
    public enum ImageType { Pre = 0, Post = 1 }

    public class ImageDefinition
    {
        public ImageType ImageType  { get; set; }
        public string    Alias      { get; set; } = string.Empty;
        public string[]  Attributes { get; set; } = Array.Empty<string>();
        // messagepropertyname is always "Target" for standard messages
    }
}
