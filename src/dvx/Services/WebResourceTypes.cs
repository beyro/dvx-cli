namespace dvx.Services
{
    /// <summary>
    /// Maps file extensions to Dataverse <c>webresourcetype</c> option-set values and classifies
    /// each type as <b>text</b> (compared as line-ending-normalized UTF-8) or <b>binary</b>
    /// (compared byte-for-byte).
    /// </summary>
    public static class WebResourceTypes
    {
        // webresourcetype option-set values.
        public const int Html = 1, Css = 2, Jscript = 3, Xml = 4, Png = 5, Jpg = 6,
                         Gif = 7, Xap = 8, Xsl = 9, Ico = 10, Svg = 11, Resx = 12;

        private static readonly Dictionary<string, int> ByExtension =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".htm"]  = Html, [".html"] = Html,
                [".css"]  = Css,
                [".js"]   = Jscript,
                [".xml"]  = Xml,
                [".png"]  = Png,
                [".jpg"]  = Jpg,  [".jpeg"] = Jpg,
                [".gif"]  = Gif,
                [".xap"]  = Xap,
                [".xsl"]  = Xsl,  [".xslt"] = Xsl,
                [".ico"]  = Ico,
                [".svg"]  = Svg,
                [".resx"] = Resx,
            };

        /// <summary>
        /// Infers the <c>webresourcetype</c> from a file's extension.
        /// Returns false (with <paramref name="type"/> = 0) for unrecognized extensions.
        /// </summary>
        public static bool TryInferType(string path, out int type)
            => ByExtension.TryGetValue(Path.GetExtension(path), out type);

        /// <summary>
        /// Text types are normalized to CRLF and compared as UTF-8 strings; everything else is
        /// compared as raw bytes.
        /// </summary>
        public static bool IsText(int type)
            => type is Html or Css or Jscript or Xml or Xsl or Svg or Resx;
    }
}
