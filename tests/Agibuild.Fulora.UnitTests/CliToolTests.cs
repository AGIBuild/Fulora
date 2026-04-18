using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Agibuild.Fulora.Cli.Commands;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class CliToolTests
{
    private static string GetCliProjectPath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "src", "Agibuild.Fulora.Cli", "Agibuild.Fulora.Cli.csproj");
    }

    /// <summary>
    /// Resolves the CLI binary that matches the current test-assembly build
    /// configuration (Debug vs Release). Matching avoids version drift: a stale
    /// Release CLI bin alongside a Debug test run can cause assembly-load
    /// mismatches when the CLI dynamically loads generated bridge assemblies
    /// built against the current-version Core.
    /// </summary>
    private static string GetCliBinaryPath()
    {
        var repoRoot = FindRepoRoot();
        var binRoot = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Cli", "bin");
        var currentConfig = GetCurrentBuildConfiguration();

        var preferred = Path.Combine(binRoot, currentConfig, "net10.0", "Agibuild.Fulora.Cli.dll");
        if (File.Exists(preferred))
            return preferred;

        var fallbackConfig = currentConfig == "Debug" ? "Release" : "Debug";
        var fallback = Path.Combine(binRoot, fallbackConfig, "net10.0", "Agibuild.Fulora.Cli.dll");
        if (File.Exists(fallback))
            return fallback;

        throw new FileNotFoundException($"CLI binary not found under {binRoot}");
    }

    /// <summary>
    /// Infers the test assembly's current build configuration by reading
    /// <see cref="AssemblyConfigurationAttribute"/> (set implicitly by the SDK
    /// per configuration). Defaults to "Debug" when the attribute is absent.
    /// </summary>
    private static string GetCurrentBuildConfiguration()
    {
        var configAttr = typeof(CliToolTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>();
        return string.IsNullOrEmpty(configAttr?.Configuration)
            ? "Debug"
            : configAttr!.Configuration;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var cliProjectPath = Path.Combine(dir, "src", "Agibuild.Fulora.Cli", "Agibuild.Fulora.Cli.csproj");
            if (File.Exists(cliProjectPath))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunCliAsync(string args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{GetCliBinaryPath()}\" {args}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? FindRepoRoot(),
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();
        return (stdoutTask.Result, stderrTask.Result, process.ExitCode);
    }

    private static (string BridgeProjectPath, string DesktopProjectPath, string WebSrcPath) CreateScaffoldWorkspace(string root, string appName)
    {
        var bridgeDir = Path.Combine(root, $"{appName}.Bridge");
        var desktopDir = Path.Combine(root, $"{appName}.Desktop");
        var webSrcDir = Path.Combine(root, $"{appName}.Web", "src");

        Directory.CreateDirectory(bridgeDir);
        Directory.CreateDirectory(desktopDir);
        Directory.CreateDirectory(webSrcDir);

        File.WriteAllText(Path.Combine(bridgeDir, $"{appName}.Bridge.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(desktopDir, $"{appName}.Desktop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(webSrcDir, "placeholder.txt"), "placeholder");

        return (
            Path.Combine(bridgeDir, $"{appName}.Bridge.csproj"),
            Path.Combine(desktopDir, $"{appName}.Desktop.csproj"),
            webSrcDir);
    }

    private static (string BridgeProjectPath, string OutputDirectory) CreateBridgeGenerationWorkspace(string root, string appName)
    {
        var repoRoot = FindRepoRoot();
        var bridgeDir = Path.Combine(root, $"{appName}.Bridge");
        var outputDir = Path.Combine(root, $"{appName}.Web", "src", "bridge", "generated");
        Directory.CreateDirectory(bridgeDir);
        Directory.CreateDirectory(outputDir);

        var coreProject = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Core", "Agibuild.Fulora.Core.csproj");
        var generatorProject = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Bridge.Generator", "Agibuild.Fulora.Bridge.Generator.csproj");

        File.WriteAllText(
            Path.Combine(bridgeDir, $"{appName}.Bridge.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{coreProject}" />
                <ProjectReference Include="{generatorProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(
            Path.Combine(bridgeDir, "IHelloService.cs"),
            """
            using Agibuild.Fulora;
            using System.Threading.Tasks;

            namespace SampleApp.Bridge;

            [JsExport]
            public interface IHelloService
            {
                Task<string> SayHello(string name);
            }
            """);

        return (Path.Combine(bridgeDir, $"{appName}.Bridge.csproj"), outputDir);
    }

    private static string InvokeDetectWebArtifactsDirectory(string bridgeProjectPath)
    {
        var method = typeof(GenerateCommand).GetMethod("DetectWebArtifactsDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [bridgeProjectPath]));
    }

    private static void MakeBridgeProjectBuildable(string bridgeProjectPath)
    {
        File.WriteAllText(
            bridgeProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
    }

    private static void WriteBridgeManifest(string generatedDir, string assemblyPath)
    {
        var artifacts = GenerateCommand.ExpectedArtifactFileNames.ToDictionary(
            fileName => fileName,
            fileName => File.ReadAllText(Path.Combine(generatedDir, fileName)),
            StringComparer.Ordinal);
        var manifest = GenerateCommand.CreateArtifactManifest("SampleApp.Bridge.csproj", generatedDir, assemblyPath, artifacts);
        GenerateCommand.WriteArtifactManifest(generatedDir, manifest);
    }

    [Fact]
    public async Task Help_shows_all_commands()
    {
        var (stdout, _, exitCode) = await RunCliAsync("--help");
        Assert.Equal(0, exitCode);
        Assert.Contains("new", stdout);
        Assert.Contains("generate", stdout);
        Assert.Contains("dev", stdout);
        Assert.Contains("add", stdout);
    }

    [Fact]
    public async Task New_command_help_shows_frontend_and_shell_preset_options()
    {
        var (stdout, _, exitCode) = await RunCliAsync("new --help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--frontend", stdout);
        Assert.Contains("--shell-preset", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void New_command_defaults_shell_preset_to_app_shell_when_omitted()
    {
        var args = NewCommand.BuildTemplateArguments("SampleApp", "react", shellPreset: null);

        Assert.Contains("--shellPreset app-shell", args, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_command_can_be_described_by_primary_path()
    {
        var (stdout, _, exitCode) = await RunCliAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Agibuild.Fulora CLI — scaffold, develop, and package apps", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("manage hybrid apps", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Add_service_command_shows_help()
    {
        var (stdout, _, exitCode) = await RunCliAsync("add service --help");
        Assert.Equal(0, exitCode);
        Assert.Contains("name", stdout);
        Assert.Contains("--import", stdout);
        Assert.Contains("--layer", stdout);
        Assert.Contains("bridge", stdout);
        Assert.Contains("framework", stdout);
        Assert.Contains("plugin", stdout);
    }

    [Fact]
    public async Task Add_service_command_requires_explicit_layer_selection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var (stdout, stderr, exitCode) = await RunCliAsync("add service SampleService", tempDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("--layer", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Add_service_command_scaffolds_framework_layer_into_framework_directories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"add service WeatherService --layer framework --bridge-project \"{workspace.BridgeProjectPath}\" --web-dir \"{workspace.WebSrcPath}\"",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("Skipped", stdout + stderr, StringComparison.Ordinal);

            var interfacePath = Path.Combine(Path.GetDirectoryName(workspace.BridgeProjectPath)!, "Framework", "IWeatherService.cs");
            var implementationPath = Path.Combine(Path.GetDirectoryName(workspace.DesktopProjectPath)!, "Framework", "WeatherService.cs");
            var tsPath = Path.Combine(workspace.WebSrcPath, "bridge", "weatherService.ts");

            Assert.True(File.Exists(interfacePath));
            Assert.True(File.Exists(implementationPath));
            Assert.True(File.Exists(tsPath));

            Assert.Contains("namespace SampleApp.Bridge.Framework;", File.ReadAllText(interfacePath), StringComparison.Ordinal);
            Assert.Contains("namespace SampleApp.Desktop.Framework;", File.ReadAllText(implementationPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Add_service_command_scaffolds_plugin_layer_and_import_skips_native_implementation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"add service AlertService --layer plugin --import --bridge-project \"{workspace.BridgeProjectPath}\" --web-dir \"{workspace.WebSrcPath}\"",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("Skipped", stdout + stderr, StringComparison.Ordinal);

            var interfacePath = Path.Combine(Path.GetDirectoryName(workspace.BridgeProjectPath)!, "Plugins", "IAlertService.cs");
            var implementationPath = Path.Combine(Path.GetDirectoryName(workspace.DesktopProjectPath)!, "Plugins", "AlertService.cs");
            var tsPath = Path.Combine(workspace.WebSrcPath, "bridge", "alertService.ts");

            Assert.True(File.Exists(interfacePath));
            Assert.False(File.Exists(implementationPath));
            Assert.True(File.Exists(tsPath));

            Assert.Contains("[JsImport]", File.ReadAllText(interfacePath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Generate_types_command_shows_help()
    {
        var (stdout, _, exitCode) = await RunCliAsync("generate types --help");
        Assert.Equal(0, exitCode);
        Assert.Contains("--project", stdout);
    }

    [Fact]
    public async Task Generate_types_command_writes_artifacts_and_manifest_end_to_end()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateBridgeGenerationWorkspace(tempDir, "SampleApp");

            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"generate types --project \"{workspace.BridgeProjectPath}\" --output \"{workspace.OutputDirectory}\"",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("bridge.d.ts", stdout, StringComparison.Ordinal);
            Assert.Contains("bridge.client.ts", stdout, StringComparison.Ordinal);
            Assert.Contains("bridge.mock.ts", stdout, StringComparison.Ordinal);
            Assert.Contains("bridge.manifest.json", stdout, StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "bridge.d.ts")));
            Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "bridge.client.ts")));
            Assert.True(File.Exists(Path.Combine(workspace.OutputDirectory, "bridge.mock.ts")));
            var manifestPath = Path.Combine(workspace.OutputDirectory, "bridge.manifest.json");
            Assert.True(File.Exists(manifestPath));

            using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifestDoc.RootElement;
            Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(Path.GetFileName(workspace.BridgeProjectPath), root.GetProperty("BridgeProjectFileName").GetString());
            Assert.Equal("net10.0", root.GetProperty("TargetFramework").GetString());
            Assert.True(root.GetProperty("Artifacts").TryGetProperty("bridge.client.ts", out _));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_types_command_can_read_all_emitted_bridge_artifacts_from_assembly()
    {
        var artifacts = GenerateCommand.ReadGeneratedArtifactsFromAssembly(Assembly.GetExecutingAssembly().Location);

        Assert.Equal(3, artifacts.Count);
        Assert.Contains("bridge.d.ts", artifacts.Keys);
        Assert.Contains("bridge.client.ts", artifacts.Keys);
        Assert.Contains("bridge.mock.ts", artifacts.Keys);
        Assert.Contains("Auto-generated", artifacts["bridge.d.ts"], StringComparison.Ordinal);
        Assert.Contains("export function createFuloraClient()", artifacts["bridge.client.ts"], StringComparison.Ordinal);
        Assert.Contains("installBridgeMock", artifacts["bridge.mock.ts"], StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_types_command_can_round_trip_bridge_artifact_manifest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var artifacts = GenerateCommand.ReadGeneratedArtifactsFromAssembly(Assembly.GetExecutingAssembly().Location);
            var manifest = GenerateCommand.CreateArtifactManifest(
                "Agibuild.Fulora.UnitTests.csproj",
                tempDir,
                Assembly.GetExecutingAssembly().Location,
                artifacts);

            GenerateCommand.WriteArtifactManifest(tempDir, manifest);
            var loaded = GenerateCommand.ReadArtifactManifest(tempDir);
            var buildIdentity = BridgeArtifactConsistency.ParseBuildIdentity(Assembly.GetExecutingAssembly().Location);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded!.SchemaVersion);
            Assert.Equal("Agibuild.Fulora.UnitTests.csproj", loaded.BridgeProjectFileName);
            Assert.Equal(Path.GetFullPath(tempDir), loaded.ArtifactDirectory);
            Assert.Equal(buildIdentity.BuildConfiguration, loaded.BuildConfiguration);
            Assert.Equal(buildIdentity.TargetFramework, loaded.TargetFramework);
            Assert.Equal(Path.GetFileName(Assembly.GetExecutingAssembly().Location), loaded.AssemblyFileName);
            Assert.Equal(3, loaded.Artifacts.Count);
            Assert.Contains("bridge.client.ts", loaded.Artifacts.Keys);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Bridge_artifact_consistency_formats_warnings_with_stable_prefix()
    {
        var formatted = BridgeArtifactConsistency.FormatWarnings(
            new[]
            {
                "missing bridge artifact manifest in /tmp/demo: bridge.manifest.json. Run `fulora generate types --project \"Demo.Bridge.csproj\"`."
            });

        Assert.Single(formatted);
        Assert.StartsWith("Bridge consistency: ", formatted[0], StringComparison.Ordinal);
        Assert.Contains("bridge.manifest.json", formatted[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_types_auto_detect_prefers_src_bridge_generated_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            var detected = InvokeDetectWebArtifactsDirectory(workspace.BridgeProjectPath);

            Assert.Equal(Path.GetFullPath(generatedDir), Path.GetFullPath(detected));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_shows_help()
    {
        var (stdout, _, exitCode) = await RunCliAsync("dev --help");
        Assert.Equal(0, exitCode);
        Assert.Contains("Vite", stdout);
    }

    [Fact]
    public async Task Dev_command_preflight_only_runs_checks_and_exits_without_starting_servers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            MakeBridgeProjectBuildable(workspace.BridgeProjectPath);
            var webDir = Path.GetDirectoryName(workspace.WebSrcPath)!;
            var generatedDir = Path.Combine(webDir, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"dev --preflight-only --web \"{webDir}\" --desktop \"{workspace.DesktopProjectPath}\"",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("Refreshing bridge artifacts", stdout, StringComparison.Ordinal);
            Assert.Contains("Preflight complete.", stdout, StringComparison.Ordinal);
            Assert.Contains("Bridge consistency:", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("Starting dev server", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_bridge_preflight_skips_when_no_bridge_project_is_present()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var originalCwd = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var exitCode = await DevCommand.PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => throw new InvalidOperationException("Should not run without a bridge project."),
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Contains("skipping bridge artifact preflight", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_bridge_preflight_builds_detected_bridge_project_before_launch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
        var originalCwd = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        string? capturedFileName = null;
        string? capturedArguments = null;
        string? capturedWorkingDirectory = null;

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var exitCode = await DevCommand.PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: stdout,
                error: stderr,
                runProcessAsync: (fileName, arguments, workingDirectory, _) =>
                {
                    capturedFileName = fileName;
                    capturedArguments = arguments;
                    capturedWorkingDirectory = workingDirectory;
                    return Task.FromResult(0);
                },
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal("dotnet", capturedFileName);
            Assert.Contains("build \"", capturedArguments, StringComparison.Ordinal);
            Assert.Contains($"{Path.GetFileName(workspace.BridgeProjectPath)}\" -v q", capturedArguments, StringComparison.Ordinal);
            Assert.Contains(
                Path.GetFileName(Path.GetDirectoryName(workspace.BridgeProjectPath)!),
                capturedWorkingDirectory,
                StringComparison.Ordinal);
            Assert.Contains("Refreshing bridge artifacts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Bridge artifacts ready", stdout.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_bridge_preflight_reports_actionable_error_when_bridge_build_fails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
        var originalCwd = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var exitCode = await DevCommand.PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(23),
                CancellationToken.None);

            Assert.Equal(23, exitCode);
            Assert.Contains("Bridge artifact generation failed", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("fulora generate types --project", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains(workspace.BridgeProjectPath, stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_bridge_preflight_warns_when_expected_generated_artifacts_are_missing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
        var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
        Directory.CreateDirectory(generatedDir);

        var originalCwd = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var exitCode = await DevCommand.PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Contains("missing generated bridge artifacts", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fulora generate types --project", stdout.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_bridge_preflight_warns_when_generated_artifacts_are_stale_relative_to_bridge_build()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
        var bridgeDir = Path.GetDirectoryName(workspace.BridgeProjectPath)!;
        var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
        Directory.CreateDirectory(generatedDir);

        foreach (var fileName in GenerateCommand.ExpectedArtifactFileNames)
        {
            var artifactPath = Path.Combine(generatedDir, fileName);
            File.WriteAllText(artifactPath, $"// {fileName}");
        }

        var assemblyDir = Path.Combine(bridgeDir, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(assemblyDir);
        var assemblyPath = Path.Combine(assemblyDir, "SampleApp.Bridge.dll");
        File.WriteAllText(assemblyPath, "stub");
        WriteBridgeManifest(generatedDir, assemblyPath);

        File.WriteAllText(Path.Combine(generatedDir, GenerateCommand.ExpectedArtifactFileNames[0]), "// mutated after manifest");

        var originalCwd = Directory.GetCurrentDirectory();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(tempDir);

            var exitCode = await DevCommand.PrepareBridgeArtifactsAsync(
                explicitBridgeProject: null,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Contains("stale generated bridge artifacts", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("fulora generate types --project", stdout.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_types_consistency_reports_manifest_bridge_project_mismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var bridgeDir = Path.GetDirectoryName(workspace.BridgeProjectPath)!;
            var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            foreach (var fileName in GenerateCommand.ExpectedArtifactFileNames)
                File.WriteAllText(Path.Combine(generatedDir, fileName), $"// {fileName}");

            var assemblyDir = Path.Combine(bridgeDir, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(assemblyDir);
            var assemblyPath = Path.Combine(assemblyDir, "SampleApp.Bridge.dll");
            File.WriteAllText(assemblyPath, "stub");

            var manifest = GenerateCommand.CreateArtifactManifest("Wrong.Bridge.csproj", generatedDir, assemblyPath,
                GenerateCommand.ExpectedArtifactFileNames.ToDictionary(
                    fileName => fileName,
                    fileName => File.ReadAllText(Path.Combine(generatedDir, fileName)),
                    StringComparer.Ordinal));
            GenerateCommand.WriteArtifactManifest(generatedDir, manifest);

            var warnings = GenerateCommand.CollectArtifactConsistencyWarnings(workspace.BridgeProjectPath);

            Assert.Contains(warnings, warning => warning.Contains("bridge project", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_types_consistency_reports_manifest_artifact_directory_mismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var bridgeDir = Path.GetDirectoryName(workspace.BridgeProjectPath)!;
            var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            foreach (var fileName in GenerateCommand.ExpectedArtifactFileNames)
                File.WriteAllText(Path.Combine(generatedDir, fileName), $"// {fileName}");

            var assemblyDir = Path.Combine(bridgeDir, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(assemblyDir);
            var assemblyPath = Path.Combine(assemblyDir, "SampleApp.Bridge.dll");
            File.WriteAllText(assemblyPath, "stub");

            var manifest = GenerateCommand.CreateArtifactManifest("SampleApp.Bridge.csproj", Path.Combine(tempDir, "other"), assemblyPath,
                GenerateCommand.ExpectedArtifactFileNames.ToDictionary(
                    fileName => fileName,
                    fileName => File.ReadAllText(Path.Combine(generatedDir, fileName)),
                    StringComparer.Ordinal));
            GenerateCommand.WriteArtifactManifest(generatedDir, manifest);

            var warnings = GenerateCommand.CollectArtifactConsistencyWarnings(workspace.BridgeProjectPath);

            Assert.Contains(warnings, warning => warning.Contains("artifact directory", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_types_consistency_reports_manifest_build_identity_mismatch()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var bridgeDir = Path.GetDirectoryName(workspace.BridgeProjectPath)!;
            var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            foreach (var fileName in GenerateCommand.ExpectedArtifactFileNames)
                File.WriteAllText(Path.Combine(generatedDir, fileName), $"// {fileName}");

            var assemblyDir = Path.Combine(bridgeDir, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(assemblyDir);
            var assemblyPath = Path.Combine(assemblyDir, "SampleApp.Bridge.dll");
            File.WriteAllText(assemblyPath, "stub");

            var manifest = GenerateCommand.CreateArtifactManifest("SampleApp.Bridge.csproj", generatedDir, assemblyPath,
                GenerateCommand.ExpectedArtifactFileNames.ToDictionary(
                    fileName => fileName,
                    fileName => File.ReadAllText(Path.Combine(generatedDir, fileName)),
                    StringComparer.Ordinal)) with
            {
                BuildConfiguration = "Release",
                TargetFramework = "net9.0"
            };
            GenerateCommand.WriteArtifactManifest(generatedDir, manifest);

            var warnings = GenerateCommand.CollectArtifactConsistencyWarnings(workspace.BridgeProjectPath);

            Assert.Contains(warnings, warning => warning.Contains("build configuration mismatch", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(warnings, warning => warning.Contains("target framework mismatch", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_command_help_shows_profile_examples()
    {
        var (stdout, _, exitCode) = await RunCliAsync("package --help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--profile", stdout);
        Assert.Contains("desktop-public", stdout);
    }

    [Fact]
    public async Task Package_command_preflight_only_runs_checks_and_exits_without_publish()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            MakeBridgeProjectBuildable(workspace.BridgeProjectPath);
            var webDir = Path.GetDirectoryName(workspace.WebSrcPath)!;
            var generatedDir = Path.Combine(webDir, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"package --project \"{workspace.DesktopProjectPath}\" --profile desktop-public --preflight-only",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("Preflight:", stdout, StringComparison.Ordinal);
            Assert.Contains("Bridge consistency:", stdout, StringComparison.Ordinal);
            Assert.Contains("Preflight complete.", stdout, StringComparison.Ordinal);
            Assert.DoesNotContain("Running: dotnet publish", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Package_command_bridge_preflight_warns_when_generated_artifacts_are_stale()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateScaffoldWorkspace(tempDir, "SampleApp");
            var bridgeDir = Path.GetDirectoryName(workspace.BridgeProjectPath)!;
            var generatedDir = Path.Combine(Path.GetDirectoryName(workspace.WebSrcPath)!, "src", "bridge", "generated");
            Directory.CreateDirectory(generatedDir);

            foreach (var fileName in GenerateCommand.ExpectedArtifactFileNames)
            {
                var artifactPath = Path.Combine(generatedDir, fileName);
                File.WriteAllText(artifactPath, $"// {fileName}");
            }

            var assemblyDir = Path.Combine(bridgeDir, "bin", "Debug", "net10.0");
            Directory.CreateDirectory(assemblyDir);
            var assemblyPath = Path.Combine(assemblyDir, "SampleApp.Bridge.dll");
            File.WriteAllText(assemblyPath, "stub");
            WriteBridgeManifest(generatedDir, assemblyPath);

            File.WriteAllText(Path.Combine(generatedDir, GenerateCommand.ExpectedArtifactFileNames[1]), "// mutated after manifest");

            var notes = PackageCommand.CollectBridgeArtifactPreflightNotes(workspace.DesktopProjectPath);

            Assert.Contains(notes, note => note.Contains("stale generated bridge artifacts", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(notes, note => note.Contains("fulora generate types --project", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_command_unknown_profile_returns_stable_error()
    {
        var (stdout, stderr, exitCode) = await RunCliAsync("package --profile unknown-profile");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Unknown package profile", stdout + stderr, StringComparison.Ordinal);
        Assert.Contains("desktop-public", stdout + stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_project_exists_and_is_packable()
    {
        var csproj = GetCliProjectPath();
        Assert.True(File.Exists(csproj), $"CLI project not found at {csproj}");

        var content = File.ReadAllText(csproj);
        Assert.Contains("PackAsTool", content);
        Assert.Contains("fulora", content);
        Assert.Contains("System.CommandLine", content);
    }
}
