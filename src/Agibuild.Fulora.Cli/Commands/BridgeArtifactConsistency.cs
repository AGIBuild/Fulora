using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Agibuild.Fulora.Cli.Commands;

internal static class BridgeArtifactConsistency
{
    internal static readonly string[] ExpectedArtifactFileNames = ["bridge.d.ts", "bridge.client.ts", "bridge.mock.ts"];
    internal const string ManifestFileName = "bridge.manifest.json";

    internal static IReadOnlyList<string> FormatWarnings(IEnumerable<string> warnings)
        => warnings.Select(warning => $"Bridge consistency: {warning}").ToArray();

    internal static GenerateCommand.BridgeArtifactManifest CreateArtifactManifest(
        string bridgeProjectFileName,
        string artifactDirectory,
        string assemblyPath,
        IReadOnlyDictionary<string, string> artifacts)
    {
        var buildIdentity = ParseBuildIdentity(assemblyPath);

        return new GenerateCommand.BridgeArtifactManifest(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTime.UtcNow.ToString("O"),
            BridgeProjectFileName: bridgeProjectFileName,
            ArtifactDirectory: Path.GetFullPath(artifactDirectory),
            BuildConfiguration: buildIdentity.BuildConfiguration,
            TargetFramework: buildIdentity.TargetFramework,
            AssemblyFileName: Path.GetFileName(assemblyPath),
            AssemblySha256: ComputeSha256(File.ReadAllBytes(assemblyPath)),
            Artifacts: artifacts.ToDictionary(
                pair => pair.Key,
                pair => ComputeSha256(Encoding.UTF8.GetBytes(pair.Value)),
                StringComparer.Ordinal));
    }

    internal static void WriteArtifactManifest(string outputDirectory, GenerateCommand.BridgeArtifactManifest manifest)
    {
        Directory.CreateDirectory(outputDirectory);
        var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, json);
    }

    internal static GenerateCommand.BridgeArtifactManifest? ReadArtifactManifest(string outputDirectory)
    {
        var manifestPath = Path.Combine(outputDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
            return null;

        return JsonSerializer.Deserialize<GenerateCommand.BridgeArtifactManifest>(File.ReadAllText(manifestPath));
    }

    internal static string? FindBuiltAssembly(string bridgeProject)
    {
        var projectDir = Path.GetDirectoryName(bridgeProject)!;
        var projectName = Path.GetFileNameWithoutExtension(bridgeProject);
        var binDir = Path.Combine(projectDir, "bin");

        if (!Directory.Exists(binDir))
            return null;

        return Directory.GetFiles(binDir, $"{projectName}.dll", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    internal static IReadOnlyList<string> CollectArtifactConsistencyWarnings(
        string bridgeProject,
        Func<string, string?> detectWebArtifactsDirectory)
    {
        var warnings = new List<string>();
        var artifactDirectory = detectWebArtifactsDirectory(bridgeProject);
        if (string.IsNullOrWhiteSpace(artifactDirectory) || !Directory.Exists(artifactDirectory))
            return warnings;

        var missingArtifacts = ExpectedArtifactFileNames
            .Where(fileName => !File.Exists(Path.Combine(artifactDirectory, fileName)))
            .ToArray();

        if (missingArtifacts.Length > 0)
        {
            warnings.Add(
                $"missing generated bridge artifacts in {artifactDirectory}: {string.Join(", ", missingArtifacts)}. Run `fulora generate types --project \"{bridgeProject}\"`.");
        }

        var assemblyPath = FindBuiltAssembly(bridgeProject);
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return warnings;

        var manifest = ReadArtifactManifest(artifactDirectory);
        if (manifest is null)
        {
            warnings.Add(
                $"missing bridge artifact manifest in {artifactDirectory}: {ManifestFileName}. Run `fulora generate types --project \"{bridgeProject}\"`.");
            return warnings;
        }

        var assemblyHash = ComputeSha256(File.ReadAllBytes(assemblyPath));
        var staleReasons = new List<string>();
        var currentBuildIdentity = ParseBuildIdentity(assemblyPath);

        var expectedBridgeProjectFileName = Path.GetFileName(bridgeProject);
        if (!string.Equals(manifest.BridgeProjectFileName, expectedBridgeProjectFileName, StringComparison.OrdinalIgnoreCase))
        {
            staleReasons.Add($"bridge project mismatch ({manifest.BridgeProjectFileName} != {expectedBridgeProjectFileName})");
        }

        var normalizedArtifactDirectory = Path.GetFullPath(artifactDirectory);
        if (!string.Equals(manifest.ArtifactDirectory, normalizedArtifactDirectory, StringComparison.OrdinalIgnoreCase))
        {
            staleReasons.Add($"artifact directory mismatch ({manifest.ArtifactDirectory} != {normalizedArtifactDirectory})");
        }

        if (!string.Equals(manifest.BuildConfiguration, currentBuildIdentity.BuildConfiguration, StringComparison.OrdinalIgnoreCase))
        {
            staleReasons.Add($"build configuration mismatch ({manifest.BuildConfiguration} != {currentBuildIdentity.BuildConfiguration})");
        }

        if (!string.Equals(manifest.TargetFramework, currentBuildIdentity.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            staleReasons.Add($"target framework mismatch ({manifest.TargetFramework} != {currentBuildIdentity.TargetFramework})");
        }

        if (!string.Equals(manifest.AssemblySha256, assemblyHash, StringComparison.OrdinalIgnoreCase))
        {
            staleReasons.Add("assembly hash does not match current bridge build");
        }

        foreach (var fileName in ExpectedArtifactFileNames)
        {
            var artifactPath = Path.Combine(artifactDirectory, fileName);
            if (!File.Exists(artifactPath))
                continue;

            if (!manifest.Artifacts.TryGetValue(fileName, out var expectedHash))
            {
                staleReasons.Add($"{fileName} is missing from {ManifestFileName}");
                continue;
            }

            var currentHash = ComputeSha256(File.ReadAllBytes(artifactPath));
            if (!string.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                staleReasons.Add($"{fileName} hash does not match manifest");
            }
        }

        if (staleReasons.Count > 0)
        {
            warnings.Add(
                $"stale generated bridge artifacts in {artifactDirectory}: {string.Join("; ", staleReasons)}. Run `fulora generate types --project \"{bridgeProject}\"` to refresh them.");
        }

        return warnings;
    }

    internal static (string BuildConfiguration, string TargetFramework) ParseBuildIdentity(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (directory is null)
            return ("unknown", "unknown");

        var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        var binIndex = Array.FindLastIndex(parts, part => string.Equals(part, "bin", StringComparison.OrdinalIgnoreCase));
        if (binIndex >= 0 && parts.Length > binIndex + 2)
            return (parts[binIndex + 1], parts[binIndex + 2]);

        return ("unknown", "unknown");
    }

    private static string ComputeSha256(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
