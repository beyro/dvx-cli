using dvx.Models;

namespace dvx.Output
{
    /// <summary>
    /// Thin console-output helper. Uses <see cref="Console.ForegroundColor"/> directly so
    /// that arbitrary strings (file paths, exception messages, etc.) are never interpreted
    /// as markup and can never cause a crash.
    /// </summary>
    internal static class Out
    {
        // ── Public methods ─────────────────────────────────────────────────────

        /// <summary>White bold-style label followed by a plain detail string.</summary>
        public static void Step(string label, string detail = "")
        {
            Colored(ConsoleColor.White, label);
            if (!string.IsNullOrEmpty(detail))
                Console.Write(" " + detail);
            Console.WriteLine();
        }

        /// <summary>Green label followed by a plain detail string.</summary>
        public static void Success(string label, string detail = "")
        {
            Colored(ConsoleColor.Green, label);
            if (!string.IsNullOrEmpty(detail))
                Console.Write(" " + detail);
            Console.WriteLine();
        }

        /// <summary>Prints <c>Error: </c> in red followed by the message on the same line.</summary>
        public static void Error(string message)
        {
            Colored(ConsoleColor.Red, "Error: ");
            Console.WriteLine(message);
        }

        /// <summary>
        /// Prints the exception message as an error.  When <paramref name="verbose"/> is
        /// <see langword="true"/>, also walks the inner-exception chain so SDK-wrapped errors
        /// surface their real cause.
        /// </summary>
        public static void Error(Exception ex, bool verbose = false)
        {
            Error(ex.Message);
            if (!verbose) return;
            var inner = ex.InnerException;
            while (inner != null)
            {
                Dim($"  ↳ [{inner.GetType().Name}] {inner.Message}");
                inner = inner.InnerException;
            }
        }

        /// <summary>Prints a plain informational line.</summary>
        public static void Info(string message) => Console.WriteLine(message);

        /// <summary>Indented sub-step indicator: cyan arrow, used inside a larger step.</summary>
        public static void SubStep(string message)
        {
            Console.Write("  ");
            Colored(ConsoleColor.Cyan, "→");
            Console.WriteLine(" " + message);
        }

        /// <summary>Muted secondary text in dark gray (verbose details, timing lines).</summary>
        public static void Dim(string message)
        {
            Colored(ConsoleColor.DarkGray, message);
            Console.WriteLine();
        }

        /// <summary>Prints <c>DRY RUN</c> in yellow followed by the message.</summary>
        public static void DryRun(string message)
        {
            Colored(ConsoleColor.Yellow, "DRY RUN");
            Console.WriteLine(" " + message);
        }

        /// <summary>Prints <c>WARN </c> in yellow followed by the message.</summary>
        public static void Warn(string message)
        {
            Colored(ConsoleColor.Yellow, "WARN ");
            Console.WriteLine(message);
        }

        /// <summary>Prints <c>ERR  </c> in red followed by the message.</summary>
        public static void Err(string message)
        {
            Colored(ConsoleColor.Red, "ERR  ");
            Console.WriteLine(message);
        }

        /// <summary>
        /// Prints the sync result summary line:
        /// <c>Created: N  Updated: N  Skipped: N  Deleted: N  (dry run)</c>
        /// with per-column colouring.
        /// </summary>
        public static void SyncSummary(int created, int updated, int skipped, int deleted, bool dryRun)
        {
            Colored(ConsoleColor.Green,    $"Created: {created}");
            Console.Write("  ");
            Colored(ConsoleColor.Yellow,   $"Updated: {updated}");
            Console.Write("  ");
            Colored(ConsoleColor.DarkGray, $"Skipped: {skipped}");
            Console.Write("  ");
            Colored(ConsoleColor.Red,      $"Deleted: {deleted}");
            if (dryRun)
            {
                Console.Write("  ");
                Colored(ConsoleColor.Yellow, "(dry run)");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Prints a <see cref="SyncResult"/>: the summary line followed by its warnings and errors.
        /// </summary>
        public static void SyncSummary(SyncResult result, bool dryRun)
        {
            SyncSummary(result.Created, result.Updated, result.Skipped, result.Deleted, dryRun);
            foreach (var w in result.Warnings) Warn(w);
            foreach (var e in result.Errors)   Err(e);
        }

        /// <summary>
        /// Prints the web resource sync summary line:
        /// <c>Created: N  Updated: N  Skipped: N  Deleted: N  Published: N  (dry run)</c>
        /// with per-column colouring.
        /// </summary>
        public static void WebResourceSummary(int created, int updated, int skipped, int deleted, int published, bool dryRun)
        {
            Colored(ConsoleColor.Green,    $"Created: {created}");
            Console.Write("  ");
            Colored(ConsoleColor.Yellow,   $"Updated: {updated}");
            Console.Write("  ");
            Colored(ConsoleColor.DarkGray, $"Skipped: {skipped}");
            Console.Write("  ");
            Colored(ConsoleColor.Red,      $"Deleted: {deleted}");
            Console.Write("  ");
            Colored(ConsoleColor.Cyan,     $"Published: {published}");
            if (dryRun)
            {
                Console.Write("  ");
                Colored(ConsoleColor.Yellow, "(dry run)");
            }
            Console.WriteLine();
        }

        // ── Private helpers ────────────────────────────────────────────────────

        private static void Colored(ConsoleColor color, string text)
        {
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = prev;
        }
    }
}
