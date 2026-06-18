namespace dvx.Config
{
    /// <summary>
    /// Decides which publisher customization prefix a deploy/sync operation should use.
    /// A target solution is authoritative: when one is supplied the prefix is derived from
    /// the solution's publisher (so component names always match the publisher they belong
    /// to). The explicitly-configured <c>publisherPrefix</c> is only a fallback for when no
    /// solution is provided; if both are present the solution wins and a warning is surfaced.
    /// </summary>
    public static class PublisherPrefixResolution
    {
        /// <summary>
        /// Resolves the effective prefix and an optional warning to display.
        /// </summary>
        /// <param name="configuredPrefix">
        /// The prefix from <c>--publisher-prefix</c> / <c>publisherPrefix</c> in config, or
        /// <see langword="null"/> when neither is set (see
        /// <see cref="ConfigLoader.ResolveConfiguredPublisherPrefix"/>).
        /// </param>
        /// <param name="solutionUniqueName">The resolved target solution, or <see langword="null"/>.</param>
        /// <param name="resolveSolutionPrefix">
        /// Looks up a solution's publisher prefix (only invoked when a solution is supplied) —
        /// typically <see cref="dvx.Services.SolutionPublisherResolver.GetCustomizationPrefix"/>.
        /// </param>
        /// <returns>The prefix to use, plus a non-null warning when a configured prefix was ignored.</returns>
        public static (string Prefix, string? Warning) Resolve(
            string? configuredPrefix,
            string? solutionUniqueName,
            Func<string, string> resolveSolutionPrefix)
        {
            if (!string.IsNullOrWhiteSpace(solutionUniqueName))
            {
                var fromSolution = resolveSolutionPrefix(solutionUniqueName!);

                var warning = !string.IsNullOrWhiteSpace(configuredPrefix)
                    ? $"A solution ('{solutionUniqueName}') and a publisher prefix ('{configuredPrefix}') " +
                      $"were both provided. Using the solution publisher's prefix ('{fromSolution}'); " +
                      "the configured publisher prefix is ignored."
                    : null;

                return (fromSolution, warning);
            }

            if (string.IsNullOrWhiteSpace(configuredPrefix))
                throw new InvalidOperationException(
                    "A publisher prefix is required. Provide a solution to derive it from the solution's " +
                    "publisher (--solution-unique-name / solutionUniqueName in config), pass " +
                    "--publisher-prefix, or set publisherPrefix in config.");

            return (configuredPrefix!, null);
        }
    }
}
