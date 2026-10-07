using System.Diagnostics;

namespace dvx.Services
{
    public record BuildResult(string? NupkgPath, string DllPath);

    public class ProjectBuilder
    {
        /// <summary>
        /// Runs <c>dotnet build</c> on the given .csproj (Release config) and returns the emitted
        /// artifact paths. Plugin package projects created by <c>pac plugin init</c> emit a
        /// <c>.nupkg</c> alongside the DLL as part of the standard build — no separate pack step
        /// needed. Throws when no <c>.nupkg</c> is produced (this is the Package-mode path).
        /// </summary>
        public BuildResult Build(string projectPath)
        {
            EnsureProjectExists(projectPath);
            RunBuild(projectPath);

            return new BuildResult(FindNupkg(projectPath), FindBuiltDll(projectPath));
        }

        /// <summary>
        /// Runs <c>dotnet build</c> (Release config) and returns the DLL, tolerating projects that
        /// emit no <c>.nupkg</c> (bare plugin assemblies deployed straight to <c>pluginassembly</c>).
        /// The <c>.nupkg</c> path is null when the project does not produce one.
        /// </summary>
        public BuildResult BuildAssembly(string projectPath)
        {
            EnsureProjectExists(projectPath);
            RunBuild(projectPath);

            return new BuildResult(FindNupkgOrNull(projectPath), FindBuiltDll(projectPath));
        }

        private static void EnsureProjectExists(string projectPath)
        {
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: '{projectPath}'", projectPath);
        }

        /// <summary>Runs the Release build. Overridable so tests can exercise artifact discovery
        /// without invoking the real <c>dotnet</c> process.</summary>
        protected virtual void RunBuild(string projectPath)
            => RunDotnet($"build \"{projectPath}\" --configuration Release --nologo");

        private static string FindNupkg(string projectPath)
            => FindNupkgOrNull(projectPath)
               ?? throw new InvalidOperationException(
                    $"No .nupkg found under '{ReleaseDirectory(projectPath)}'. " +
                    "Ensure the project is a Dataverse plugin package project created with 'pac plugin init'. " +
                    "The build should automatically emit a .nupkg alongside the DLL.");

        private static string? FindNupkgOrNull(string projectPath)
        {
            var releaseDir  = ReleaseDirectory(projectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            var candidates = Directory.GetFiles(releaseDir, "*.nupkg", SearchOption.AllDirectories);
            if (candidates.Length == 0)
                return null;

            // Prefer the nupkg whose versioned stem starts with the project name, e.g.
            // MyPlugin.1.0.0.nupkg → stem "MyPlugin.1.0.0" starts with "MyPlugin".
            // Falls back to the first candidate if nothing matches.
            var match = candidates.FirstOrDefault(p =>
                Path.GetFileNameWithoutExtension(p)
                    .StartsWith(projectName, StringComparison.OrdinalIgnoreCase));

            return match ?? candidates[0];
        }

        /// <summary>
        /// Finds the compiled DLL in the project's bin/Release output directory.
        /// Works for any single-targeted framework (net462, net6.0, net8.0, …).
        /// </summary>
        private static string FindBuiltDll(string projectPath)
        {
            var releaseDir  = ReleaseDirectory(projectPath);
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            var candidates = Directory.GetFiles(releaseDir, $"{projectName}.dll", SearchOption.AllDirectories);

            if (candidates.Length == 0)
                throw new InvalidOperationException(
                    $"Could not find '{projectName}.dll' under '{releaseDir}'. " +
                    "Ensure the project name matches the assembly name.");

            // Single-targeted plugin projects produce exactly one DLL; take it.
            return candidates[0];
        }

        private static string ReleaseDirectory(string projectPath)
        {
            var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var releaseDir = Path.Combine(projectDir, "bin", "Release");

            if (!Directory.Exists(releaseDir))
                throw new InvalidOperationException(
                    $"Build output directory not found: '{releaseDir}'. " +
                    "Ensure the project built successfully.");

            return releaseDir;
        }

        private static void RunDotnet(string args)
        {
            var psi = new ProcessStartInfo("dotnet", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start dotnet process.");

            // Read both pipes concurrently — sequential reads can deadlock when buffers fill.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"dotnet {args} failed (exit {proc.ExitCode}):\n{stdout}\n{stderr}");
        }
    }
}
