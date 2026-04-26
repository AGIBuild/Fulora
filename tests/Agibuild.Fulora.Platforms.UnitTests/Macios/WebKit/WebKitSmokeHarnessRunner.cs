using System.Diagnostics;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WebKitSmokeHarnessCollection
{
    public const string Name = "WebKitSmokeHarness";
}

internal static class WebKitSmokeHarnessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<string> RunAsync(string caseId)
    {
        var harnessPath = GetHarnessPath();
        Assert.True(File.Exists(harnessPath), $"WebKit smoke harness was not built: {harnessPath}");

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add(harnessPath);
        process.StartInfo.ArgumentList.Add("--case");
        process.StartInfo.ArgumentList.Add(caseId);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"WebKit smoke harness timed out after {Timeout}: {caseId}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        Assert.Equal(0, process.ExitCode);
        Assert.Contains($"\"case\":\"{caseId}\"", stdout, StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        return stdout;
    }

    private static string GetHarnessPath()
    {
        var root = FindRepositoryRoot();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal)
            ? "Release"
            : "Debug";

        return Path.Combine(
            root,
            "tests",
            "Agibuild.Fulora.Platforms.WebKitSmokeHarness",
            "bin",
            configuration,
            "net10.0",
            "Agibuild.Fulora.Platforms.WebKitSmokeHarness.dll");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Agibuild.Fulora.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
