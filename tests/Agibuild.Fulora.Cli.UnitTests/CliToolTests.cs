using System.Reflection;
using System.Text.Json;
using System.CommandLine.Parsing;
using Agibuild.Fulora.Cli;
using Agibuild.Fulora.Cli.Commands;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

[Collection(StatefulIOCollection.Name)]
public class CliToolTests
{
    private static string GetCliProjectPath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "src", "Agibuild.Fulora.Cli", "Agibuild.Fulora.Cli.csproj");
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
        var originalCwd = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Directory.SetCurrentDirectory(workingDirectory ?? FindRepoRoot());
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var tokens = CommandLineParser.SplitCommandLine(args).ToArray();
            var exitCode = await CliRootCommand.Create().Parse(tokens).InvokeAsync();
            return (stdout.ToString(), stderr.ToString(), exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalCwd);
        }
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

    private static (FakeFileSystem FileSystem, string WorkspaceRoot, string BridgeProjectPath, string DesktopProjectPath, string OutputDirectory) CreateConfigDrivenWorkspace()
    {
        var repoRoot = FindRepoRoot();
        var root = Path.GetFullPath("/virtual/config-workspace");
        var webDir = Path.Combine(root, "apps", "frontend-app");
        var docsDir = Path.Combine(root, "apps", "docs-site");
        var bridgeDir = Path.Combine(root, "apps", "contracts");
        var desktopDir = Path.Combine(root, "apps", "native-host");
        var outputDir = Path.Combine(webDir, "src", "host-generated");
        var bridgeProjectPath = Path.Combine(bridgeDir, "HostContracts.csproj");
        var desktopProjectPath = Path.Combine(desktopDir, "Host.csproj");
        var fileSystem = new FakeFileSystem();

        fileSystem.AddDirectory(webDir);
        fileSystem.AddDirectory(docsDir);
        fileSystem.AddDirectory(bridgeDir);
        fileSystem.AddDirectory(desktopDir);
        fileSystem.AddDirectory(outputDir);

        fileSystem.AddFile(Path.Combine(webDir, "package.json"), """{"name":"frontend-app","private":true}""");
        fileSystem.AddFile(Path.Combine(docsDir, "package.json"), """{"name":"docs-site","private":true}""");

        var coreProject = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Core", "Agibuild.Fulora.Core.csproj");
        var generatorProject = Path.Combine(repoRoot, "src", "Agibuild.Fulora.Bridge.Generator", "Agibuild.Fulora.Bridge.Generator.csproj");

        fileSystem.AddFile(
            bridgeProjectPath,
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

        fileSystem.AddFile(
            Path.Combine(bridgeDir, "IHostPingService.cs"),
            """
            using Agibuild.Fulora;
            using System.Threading.Tasks;

            namespace SampleApp.Contracts;

            [JsExport]
            public interface IHostPingService
            {
                Task<string> Ping();
            }
            """);

        fileSystem.AddFile(
            desktopProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        FuloraWorkspaceConfigResolver.Save(
            root,
            new FuloraWorkspaceConfig
            {
                Web = new FuloraWorkspaceConfig.WebSection
                {
                    Root = "./apps/frontend-app",
                    Command = "npm run dev",
                    DevServerUrl = "http://localhost:5173",
                    GeneratedDir = "./apps/frontend-app/src/host-generated"
                },
                Bridge = new FuloraWorkspaceConfig.BridgeSection
                {
                    Project = "./apps/contracts/HostContracts.csproj"
                },
                Desktop = new FuloraWorkspaceConfig.DesktopSection
                {
                    Project = "./apps/native-host/Host.csproj"
                }
            },
            fileSystem);

        return (fileSystem, root, bridgeProjectPath, desktopProjectPath, outputDir);
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
        Assert.Contains("attach", stdout);
        Assert.Contains("generate", stdout);
        Assert.Contains("dev", stdout);
        Assert.Contains("add", stdout);
    }

    [Fact]
    public async Task Attach_web_help_shows_web_desktop_bridge_and_framework_options()
    {
        var (stdout, _, exitCode) = await RunCliAsync("attach web --help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--web", stdout, StringComparison.Ordinal);
        Assert.Contains("--desktop", stdout, StringComparison.Ordinal);
        Assert.Contains("--bridge", stdout, StringComparison.Ordinal);
        Assert.Contains("--framework", stdout, StringComparison.Ordinal);
        Assert.Contains("--web-command", stdout, StringComparison.Ordinal);
        Assert.Contains("--dev-server-url", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attach_web_requires_an_existing_web_project_root()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        var webDir = Path.Combine(tempDir, "existing-web");
        Directory.CreateDirectory(webDir);

        try
        {
            var (stdout, stderr, exitCode) = await RunCliAsync($"attach web --web \"{webDir}\" --framework react", tempDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("Fulora could not find your web project root", stdout + stderr, StringComparison.Ordinal);
            Assert.Contains("package.json", stdout + stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Attach_web_scaffolds_fulora_owned_files_and_writes_workspace_config()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-attach-{Guid.NewGuid():N}");
        var webDir = Path.Combine(tempDir, "apps", "product-web");
        var desktopDir = Path.Combine(tempDir, "apps", "Product.Desktop");
        var bridgeDir = Path.Combine(tempDir, "apps", "Product.Bridge");
        Directory.CreateDirectory(webDir);
        File.WriteAllText(Path.Combine(webDir, "package.json"), """{"name":"product-web","private":true}""");

        try
        {
            const string webArg = "./apps/product-web";
            const string desktopArg = "./apps/Product.Desktop";
            const string bridgeArg = "./apps/Product.Bridge";
            var (stdout, stderr, exitCode) = await RunCliAsync(
                $"attach web --web \"{webArg}\" --desktop \"{desktopArg}\" --bridge \"{bridgeArg}\" --framework react --web-command \"npm run dev\" --dev-server-url http://localhost:5173",
                tempDir);

            Assert.Equal(0, exitCode);
            Assert.DoesNotContain("not implemented", stdout + stderr, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(tempDir, "fulora.json")));
            Assert.True(File.Exists(Path.Combine(webDir, "src", "bridge", "client.ts")));
            Assert.True(File.Exists(Path.Combine(webDir, "src", "bridge", "services.ts")));
            Assert.True(Directory.Exists(Path.Combine(webDir, "src", "bridge", "generated")));
            Assert.True(File.Exists(Path.Combine(bridgeDir, "Product.Bridge.csproj")));
            Assert.True(File.Exists(Path.Combine(desktopDir, "Product.Desktop.csproj")));

            var configJson = File.ReadAllText(Path.Combine(tempDir, "fulora.json"));
            Assert.Contains("./apps/product-web", configJson, StringComparison.Ordinal);
            Assert.Contains("./apps/Product.Bridge/Product.Bridge.csproj", configJson, StringComparison.Ordinal);
            Assert.Contains("./apps/Product.Desktop/Product.Desktop.csproj", configJson, StringComparison.Ordinal);

            Assert.Contains("npm run dev", stdout, StringComparison.Ordinal);
            Assert.Contains("fulora dev", stdout, StringComparison.Ordinal);
            Assert.Contains("fulora generate types", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
    public async Task Generate_types_command_writes_artifacts_and_manifest_via_execute_helper()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var workspace = CreateBridgeGenerationWorkspace(tempDir, "SampleApp");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = await GenerateCommand.ExecuteTypesAsync(
                explicitProject: workspace.BridgeProjectPath,
                explicitOutput: workspace.OutputDirectory,
                workingDirectory: tempDir,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                findBuiltAssembly: _ => Assembly.GetExecutingAssembly().Location,
                fileSystem: RealFileSystem.Instance,
                ct: CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.Contains("bridge.d.ts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("bridge.client.ts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("bridge.mock.ts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("bridge.manifest.json", stdout.ToString(), StringComparison.Ordinal);

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
    public void Generate_types_resolution_uses_workspace_config_when_arguments_are_omitted()
    {
        var workspace = CreateConfigDrivenWorkspace();
        Assert.Equal(workspace.BridgeProjectPath, GenerateCommand.ResolveBridgeProject(explicitProject: null, workspace.WorkspaceRoot, workspace.FileSystem));
        Assert.Equal(workspace.OutputDirectory, GenerateCommand.ResolveOutputDirectory(explicitOutput: null, workspace.BridgeProjectPath, workspace.WorkspaceRoot, workspace.FileSystem));
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
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = await DevCommand.ExecuteAsync(
                explicitWebProject: webDir,
                explicitDesktopProject: workspace.DesktopProjectPath,
                npmScript: "dev",
                preflightOnly: true,
                workingDirectory: tempDir,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                runUntilCancelledAsync: (_, _, _, _, _) => Task.CompletedTask,
                ct: CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.Contains("Fulora Dev Status", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Web App", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Desktop Host", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Bridge Artifacts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Mock Mode", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Package Readiness", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Refreshing bridge artifacts", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Preflight complete.", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("Bridge consistency:", stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Starting dev server", stdout.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Dev_command_resolution_uses_workspace_config_when_detection_is_ambiguous()
    {
        var workspace = CreateConfigDrivenWorkspace();
        Assert.Equal(Path.Combine(workspace.WorkspaceRoot, "apps", "frontend-app"), DevCommand.ResolveWebProject(explicitWebProject: null, workspace.WorkspaceRoot, workspace.FileSystem));
        Assert.Equal(workspace.DesktopProjectPath, DevCommand.ResolveDesktopProject(explicitDesktopProject: null, workspace.WorkspaceRoot, workspace.FileSystem));
        Assert.Equal(workspace.BridgeProjectPath, DevCommand.ResolveBridgeProject(workspace.WorkspaceRoot, workspace.FileSystem));
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
    public async Task Dev_command_missing_web_project_reports_attach_web_next_step()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var desktopProject = Path.Combine(tempDir, "Demo.Desktop.csproj");
            File.WriteAllText(desktopProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = await DevCommand.ExecuteAsync(
                explicitWebProject: null,
                explicitDesktopProject: desktopProject,
                npmScript: "dev",
                preflightOnly: true,
                workingDirectory: tempDir,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                runUntilCancelledAsync: (_, _, _, _, _) => Task.CompletedTask,
                ct: CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("Could not find web project", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("fulora attach web", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Dev_command_missing_desktop_project_reports_attach_web_next_step()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var webDir = Path.Combine(tempDir, "web");
            Directory.CreateDirectory(webDir);
            File.WriteAllText(Path.Combine(webDir, "package.json"), "{\"name\":\"demo-web\"}");
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();

            var exitCode = await DevCommand.ExecuteAsync(
                explicitWebProject: webDir,
                explicitDesktopProject: null,
                npmScript: "dev",
                preflightOnly: true,
                workingDirectory: tempDir,
                output: stdout,
                error: stderr,
                runProcessAsync: (_, _, _, _) => Task.FromResult(0),
                runUntilCancelledAsync: (_, _, _, _, _) => Task.CompletedTask,
                ct: CancellationToken.None);

            Assert.Equal(1, exitCode);
            Assert.Contains("Could not find Desktop .csproj", stderr.ToString(), StringComparison.Ordinal);
            Assert.Contains("fulora attach web", stderr.ToString(), StringComparison.Ordinal);
        }
        finally
        {
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
    public void Package_command_resolution_uses_workspace_config_when_project_is_omitted()
    {
        var workspace = CreateConfigDrivenWorkspace();
        Assert.Equal(workspace.DesktopProjectPath, PackageCommand.ResolveProjectPath(explicitProject: null, workspace.WorkspaceRoot, workspace.FileSystem));
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
