using System.Diagnostics;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

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
            var gitPath = Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunCliAsync(string args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo("dotnet", $"run --project \"{GetCliProjectPath()}\" -- {args}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? FindRepoRoot(),
        };

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (stdout, stderr, process.ExitCode);
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
    public async Task New_command_help_shows_frontend_but_not_required_shell_preset()
    {
        var (stdout, _, exitCode) = await RunCliAsync("new --help");

        Assert.Equal(0, exitCode);
        Assert.Contains("--frontend", stdout);
        Assert.DoesNotContain("Required", stdout[(stdout.IndexOf("--shell-preset", StringComparison.Ordinal))..], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_command_can_be_described_by_primary_path()
    {
        var (stdout, _, exitCode) = await RunCliAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("scaffold", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("develop", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package", stdout, StringComparison.OrdinalIgnoreCase);
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
    public async Task Dev_command_shows_help()
    {
        var (stdout, _, exitCode) = await RunCliAsync("dev --help");
        Assert.Equal(0, exitCode);
        Assert.Contains("Vite", stdout);
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
