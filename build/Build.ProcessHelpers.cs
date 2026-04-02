using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Build;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private static readonly JsonSerializerOptions WriteIndentedJsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions GovernanceCamelCaseJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly ProcessRunner Runner = new();

    private static void WriteJsonReport(AbsolutePath path, object payload)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(payload, WriteIndentedJsonOptions));
    }

    private static void WriteGovernanceReport(AbsolutePath path, object payload)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(payload, GovernanceCamelCaseJsonOptions));
    }

    private sealed class TempDirectoryScope : IDisposable
    {
        private readonly string _path;

        private TempDirectoryScope(string path)
        {
            _path = path;
            Directory.CreateDirectory(_path);
        }

        public string Path => _path;

        public static TempDirectoryScope Create(string prefix)
        {
            var path = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"{prefix}-{Guid.NewGuid():N}"));
            return new TempDirectoryScope(path);
        }

        public void Dispose()
        {
            if (!Directory.Exists(_path))
                return;

            try
            {
                Directory.Delete(_path, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    // ──────────────────────────── Process convenience wrappers ────────────────────────────

    private static async Task<string> RunProcessAsync(
        string fileName,
        string[] arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var result = await Runner.RunAsync(
            new ProcessCommand(fileName, arguments, workingDirectory, timeout));

        if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.StandardError))
            Serilog.Log.Warning("Process stderr: {Error}", result.StandardError.Trim());

        return result.StandardOutput;
    }

    private static async Task<string> RunProcessCaptureAllAsync(
        string fileName,
        string[] arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var result = await Runner.RunAsync(
            new ProcessCommand(fileName, arguments, workingDirectory, timeout));

        return CombineOutput(result.StandardOutput, result.StandardError);
    }

    private static async Task<string> RunProcessCheckedAsync(
        string fileName,
        string[] arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var result = await Runner.RunAsync(
            new ProcessCommand(fileName, arguments, workingDirectory, timeout));

        var combined = CombineOutput(result.StandardOutput, result.StandardError);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Process '{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}.\n{combined}");
        }

        return combined;
    }

    private static async Task<string> RunProcessStdoutCheckedAsync(
        string fileName,
        string[] arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var result = await Runner.RunAsync(
            new ProcessCommand(fileName, arguments, workingDirectory, timeout));

        if (!result.IsSuccess)
        {
            var combined = CombineOutput(result.StandardOutput, result.StandardError);
            throw new InvalidOperationException(
                $"Process '{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}.\n{combined}");
        }

        return result.StandardOutput;
    }

    // ──────────────────────────── npm / package-manager helpers ────────────────────────────

    private static Task<string> RunNpmCaptureAllAsync(
        string[] arguments,
        string workingDirectory,
        TimeSpan? timeout = null)
    {
        return OperatingSystem.IsWindows()
            ? RunProcessCaptureAllAsync(
                "cmd.exe",
                ["/d", "/s", "/c", $"npm {string.Join(' ', arguments)}"],
                workingDirectory,
                timeout)
            : RunProcessCaptureAllAsync("npm", arguments, workingDirectory, timeout);
    }

    private static Task<string> RunNpmStdoutAsync(
        string[] arguments,
        string workingDirectory,
        TimeSpan? timeout = null)
    {
        return OperatingSystem.IsWindows()
            ? RunProcessAsync(
                "cmd.exe",
                ["/d", "/s", "/c", $"npm {string.Join(' ', arguments)}"],
                workingDirectory,
                timeout)
            : RunProcessAsync("npm", arguments, workingDirectory, timeout);
    }

    private static Task<string> RunNpmCheckedAsync(
        string[] arguments,
        string workingDirectory,
        TimeSpan? timeout = null)
    {
        return OperatingSystem.IsWindows()
            ? RunProcessCheckedAsync(
                "cmd.exe",
                ["/d", "/s", "/c", $"npm {string.Join(' ', arguments)}"],
                workingDirectory,
                timeout)
            : RunProcessCheckedAsync("npm", arguments, workingDirectory, timeout);
    }

    private static async Task<bool> IsToolAvailableAsync(
        string toolName,
        TimeSpan? timeout = null,
        string[]? checkArguments = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        checkArguments ??= ["--version"];
        var defaultVersionCommand = $"{toolName} --version";

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var command = checkArguments.Length == 1 && checkArguments[0] == "--version"
                    ? defaultVersionCommand
                    : $"{toolName} {string.Join(' ', checkArguments)}".TrimEnd();
                await RunProcessCheckedAsync(
                    "cmd.exe",
                    ["/d", "/s", "/c", command],
                    timeout: timeout);
            }
            else
            {
                await RunProcessCheckedAsync(
                    toolName,
                    checkArguments,
                    timeout: timeout);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Task<string> RunPmInstallAsync(string pm, string workingDirectory)
    {
        return OperatingSystem.IsWindows()
            ? RunProcessCheckedAsync(
                "cmd.exe",
                ["/d", "/s", "/c", $"{pm} install"],
                workingDirectory,
                TimeSpan.FromMinutes(2))
            : RunProcessCheckedAsync(pm, ["install"], workingDirectory, TimeSpan.FromMinutes(2));
    }

    // ──────────────────────────── Shared helpers ────────────────────────────

    private static string CombineOutput(string stdout, string stderr)
    {
        return string.Join('\n',
            new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
