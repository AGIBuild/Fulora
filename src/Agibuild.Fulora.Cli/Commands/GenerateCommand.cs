using System.CommandLine;
using System.Reflection;

namespace Agibuild.Fulora.Cli.Commands;

internal static class GenerateCommand
{
    internal static readonly string[] ExpectedArtifactFileNames = BridgeArtifactConsistency.ExpectedArtifactFileNames;
    internal const string ManifestFileName = BridgeArtifactConsistency.ManifestFileName;

    public static Command Create()
    {
        var group = new Command("generate") { Description = "Bridge and code generation commands" };
        group.Aliases.Add("gen");
        group.Subcommands.Add(CreateTypesSubcommand());
        return group;
    }

    private static Command CreateTypesSubcommand()
    {
        var projectOpt = new Option<string?>("--project", "-p")
        {
            Description = "Path to the Bridge .csproj (auto-detected if omitted)"
        };
        var outputOpt = new Option<string?>("--output", "-o")
        {
            Description = "Output directory for generated bridge artifacts (default: auto-detected web project)"
        };

        var command = new Command("types") { Description = "Generate bridge TypeScript artifacts from C# bridge interfaces" };
        command.Options.Add(projectOpt);
        command.Options.Add(outputOpt);

        command.SetAction(async (parseResult, ct) =>
        {
            var project = parseResult.GetValue(projectOpt);
            var output = parseResult.GetValue(outputOpt);

            var bridgeProject = project ?? DetectBridgeProject();
            if (bridgeProject is null)
            {
                Console.Error.WriteLine("Could not find a Bridge .csproj. Use --project to specify one.");
                return 1;
            }

            Console.WriteLine($"Building {Path.GetFileName(bridgeProject)} to generate bridge TypeScript artifacts...");

            var exitCode = await NewCommand.RunProcessAsync("dotnet", $"build \"{bridgeProject}\" -v q -m:1 -nodeReuse:false", ct: ct);
            if (exitCode != 0)
            {
                Console.Error.WriteLine($"Build failed with exit code {exitCode}.");
                return exitCode;
            }

            var assemblyPath = FindBuiltAssembly(bridgeProject);
            if (assemblyPath is null)
            {
                Console.Error.WriteLine("Could not find the built Bridge assembly after compilation.");
                Console.Error.WriteLine("Ensure the project builds successfully and targets a concrete framework output.");
                return 1;
            }

            var outDir = output ?? DetectWebTypesDirectory(bridgeProject);
            if (outDir is null)
            {
                Console.Error.WriteLine("Could not detect web project types directory. Use --output to specify.");
                return 1;
            }

            Directory.CreateDirectory(outDir);
            IReadOnlyDictionary<string, string> artifacts;
            try
            {
                artifacts = ReadGeneratedArtifactsFromAssembly(assemblyPath);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Ensure the project references Agibuild.Fulora.Bridge.Generator and exposes BridgeTypeScriptDeclarations.");
                return 1;
            }

            foreach (var artifact in artifacts)
            {
                var destPath = Path.Combine(outDir, artifact.Key);
                File.WriteAllText(destPath, artifact.Value);
                Console.WriteLine($"TypeScript artifact written to {destPath}");
            }

            var manifest = CreateArtifactManifest(Path.GetFileName(bridgeProject), outDir, assemblyPath, artifacts);
            WriteArtifactManifest(outDir, manifest);
            Console.WriteLine($"Bridge artifact manifest written to {Path.Combine(outDir, ManifestFileName)}");
            return 0;
        });

        return command;
    }

    internal static string? DetectBridgeProject()
        => DetectBridgeProject(Directory.GetCurrentDirectory());

    internal static string? DetectBridgeProject(string cwd)
    {
        var candidates = Directory.GetFiles(cwd, "*.Bridge.csproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(cwd, "*Bridge*.csproj", SearchOption.AllDirectories))
            .Distinct()
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            > 1 => candidates.FirstOrDefault(p => p.Contains("Bridge", StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
    }

    internal static IReadOnlyDictionary<string, string> ReadGeneratedArtifactsFromAssembly(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var declarationsType = assembly.GetTypes().FirstOrDefault(t => t.Name == "BridgeTypeScriptDeclarations");
        if (declarationsType is null)
            throw new InvalidOperationException("No BridgeTypeScriptDeclarations found in the built assembly.");

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ExpectedArtifactFileNames[0]] = ReadArtifactField(declarationsType, "All"),
            [ExpectedArtifactFileNames[1]] = ReadArtifactField(declarationsType, "Client"),
            [ExpectedArtifactFileNames[2]] = ReadArtifactField(declarationsType, "Mock"),
        };
    }

    internal static BridgeArtifactManifest CreateArtifactManifest(
        string bridgeProjectFileName,
        string artifactDirectory,
        string assemblyPath,
        IReadOnlyDictionary<string, string> artifacts)
        => BridgeArtifactConsistency.CreateArtifactManifest(bridgeProjectFileName, artifactDirectory, assemblyPath, artifacts);

    internal static void WriteArtifactManifest(string outputDirectory, BridgeArtifactManifest manifest)
        => BridgeArtifactConsistency.WriteArtifactManifest(outputDirectory, manifest);

    internal static BridgeArtifactManifest? ReadArtifactManifest(string outputDirectory)
        => BridgeArtifactConsistency.ReadArtifactManifest(outputDirectory);

    internal static string? FindBuiltAssembly(string bridgeProject)
        => BridgeArtifactConsistency.FindBuiltAssembly(bridgeProject);

    internal static string? DetectWebArtifactsDirectory(string bridgeProject)
        => DetectWebTypesDirectory(bridgeProject);

    internal static IReadOnlyList<string> CollectArtifactConsistencyWarnings(string bridgeProject)
        => BridgeArtifactConsistency.CollectArtifactConsistencyWarnings(bridgeProject, DetectWebTypesDirectory);

    private static string? DetectWebTypesDirectory(string bridgeProject)
    {
        var solutionDir = Path.GetDirectoryName(bridgeProject);
        if (solutionDir is null) return null;

        var parent = Directory.GetParent(solutionDir)?.FullName;
        if (parent is null) return null;

        var webDirs = Directory.GetDirectories(parent)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name.Contains("Web", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Vite", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (webDirs.Length == 0)
            return null;

        var webDir = webDirs[0];
        var generatedBridgeDir = Path.Combine(webDir, "src", "bridge", "generated");
        if (Directory.Exists(generatedBridgeDir))
            return generatedBridgeDir;

        var srcBridge = Path.Combine(webDir, "src", "bridge");
        return Directory.Exists(srcBridge) ? srcBridge : Path.Combine(webDir, "src", "types");
    }

    private static string ReadArtifactField(Type declarationsType, string fieldName)
    {
        var field = declarationsType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        if (field?.GetValue(null) is string content && !string.IsNullOrWhiteSpace(content))
            return content;

        throw new InvalidOperationException($"BridgeTypeScriptDeclarations.{fieldName} was not found or was empty.");
    }

    internal sealed record BridgeArtifactManifest(
        int SchemaVersion,
        string GeneratedAtUtc,
        string BridgeProjectFileName,
        string ArtifactDirectory,
        string BuildConfiguration,
        string TargetFramework,
        string AssemblyFileName,
        string AssemblySha256,
        IReadOnlyDictionary<string, string> Artifacts);
}
