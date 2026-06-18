using System.Diagnostics;

namespace dvx.Services
{
    public record BuildResult(string NupkgPath, string DllPath);

    public class ProjectBuilder
    {
        /// <summary>
        /// Runs <c>dotnet build</c> on the given .csproj (Release config).
        /// Plugin package projects created by <c>pac plugin init</c> emit a <c>.nupkg</c>
        /// alongside the DLL as part of the standard build — no separate pack step needed.
        /// Returns paths to both files for upload and reflection-based step discovery.
        /// </summary>
        public BuildResult Build(string projectPath)
        {
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: '{projectPath}'", projectPath);

            RunDotnet($"build \"{projectPath}\" --configuration Release --nologo");

            return new BuildResult(FindNupkg(projectPath), FindBuiltDll(projectPath));
        }

        private static string FindNupkg(string projectPath)
        {
            var projectDir  = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var releaseDir  = Path.Combine(projectDir, "bin", "Release");

            if (!Directory.Exists(releaseDir))
                throw new InvalidOperationException(
                    $"Build output directory not found: '{releaseDir}'. " +
                    "Ensure the project built successfully.");

            var candidates = Directory.GetFiles(releaseDir, "*.nupkg", SearchOption.AllDirectories);

            if (candidates.Length == 0)
                throw new InvalidOperationException(
                    $"No .nupkg found under '{releaseDir}'. " +
                    "Ensure the project is a Dataverse plugin package project created with 'pac plugin init'. " +
                    "The build should automatically emit a .nupkg alongside the DLL.");

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
            var projectDir  = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var releaseDir  = Path.Combine(projectDir, "bin", "Release");

            if (!Directory.Exists(releaseDir))
                throw new InvalidOperationException(
                    $"Build output directory not found: '{releaseDir}'. " +
                    "Ensure the project built successfully.");

            var candidates = Directory.GetFiles(releaseDir, $"{projectName}.dll", SearchOption.AllDirectories);

            if (candidates.Length == 0)
                throw new InvalidOperationException(
                    $"Could not find '{projectName}.dll' under '{releaseDir}'. " +
                    "Ensure the project name matches the assembly name.");

            // Single-targeted plugin projects produce exactly one DLL; take it.
            return candidates[0];
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
