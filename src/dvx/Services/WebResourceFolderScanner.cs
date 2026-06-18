using dvx.Models;

namespace dvx.Services
{
    /// <summary>
    /// Walks a folder recursively and produces a <see cref="WebResourceDefinition"/> for every file
    /// with a recognized web-resource extension. The Dataverse name is derived as
    /// <c>{prefix}_/{relativePath}</c> using forward slashes, where <c>prefix</c> is the
    /// publisher's customization prefix. Files with unrecognized
    /// extensions, dotfiles/dotfolders, source maps, and known build/dependency directories
    /// (<c>node_modules</c>, <c>bin</c>, <c>obj</c>, <c>.git</c>) are skipped.
    /// </summary>
    public class WebResourceFolderScanner
    {
        private static readonly string[] IgnoredDirSegments = { "node_modules", ".git", "bin", "obj" };

        /// <summary>
        /// Scans <paramref name="folder"/> and returns the discovered definitions.
        /// Files skipped because of an unrecognized extension are reported (relative paths) via
        /// <paramref name="skipped"/> for verbose logging.
        /// </summary>
        public IReadOnlyList<WebResourceDefinition> Scan(
            string folder, string prefix, out IReadOnlyList<string> skipped)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Web resource folder not found: '{folder}'.");

            var root        = Path.GetFullPath(folder);
            var results     = new List<WebResourceDefinition>();
            var skippedList = new List<string>();

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');

                if (IsIgnored(rel))
                    continue;

                if (!WebResourceTypes.TryInferType(path, out var type))
                {
                    skippedList.Add(rel);
                    continue;
                }

                results.Add(new WebResourceDefinition
                {
                    Name      = $"{prefix}_/{rel}",
                    LocalPath = path,
                    Type      = type,
                });
            }

            skipped = skippedList;
            return results;
        }

        private static bool IsIgnored(string relForwardSlash)
        {
            if (relForwardSlash.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var seg in relForwardSlash.Split('/'))
            {
                if (seg.StartsWith('.'))
                    return true;
                if (IgnoredDirSegments.Contains(seg, StringComparer.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
