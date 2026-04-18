using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

public sealed class WebView2TeardownStabilityIntegrationTests
{
    [Fact]
    public async Task WebView2_teardown_stress_does_not_emit_chromium_teardown_markers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var desktopProject = Path.Combine(
            repoRoot,
            "tests",
            "Agibuild.Fulora.Integration.Tests",
            "Agibuild.Fulora.Integration.Tests.Desktop",
            "Agibuild.Fulora.Integration.Tests.Desktop.csproj");

        Assert.True(File.Exists(desktopProject), $"Desktop integration test project not found: {desktopProject}");

        const int iterations = 10;
        var buildArgs = $"build \"{desktopProject}\" --configuration Debug";
        var runArgs =
            $"run --project \"{desktopProject}\" --configuration Debug --no-build --no-restore " +
            "-- " +
            $"--wv2-teardown-stress --wv2-teardown-iterations {iterations}";
        var userDataFolder = Path.Combine(
            repoRoot,
            "artifacts",
            "test-results",
            "wv2-teardown-profile",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userDataFolder);

        // Build and runtime are split on purpose: teardown correctness should not be coupled
        // to cold-start compile cost fluctuations in CI environments.
        var buildResult = await RunProcessCaptureAsync(
            fileName: "dotnet",
            arguments: buildArgs,
            workingDirectory: repoRoot,
            timeout: TimeSpan.FromMinutes(3),
            ct: TestContext.Current.CancellationToken);

        Assert.False(buildResult.TimedOut, FormatFailureMessage("build", buildResult, buildArgs));
        Assert.True(buildResult.ExitCode == 0, FormatFailureMessage("build", buildResult, buildArgs));

        var result = await RunProcessCaptureAsync(
            fileName: "dotnet",
            arguments: runArgs,
            workingDirectory: repoRoot,
            timeout: TimeSpan.FromMinutes(4),
            environmentVariables: new Dictionary<string, string>
            {
                ["WEBVIEW2_USER_DATA_FOLDER"] = userDataFolder
            },
            ct: TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut, FormatFailureMessage("run", result, runArgs));

        var combinedOutput = $"{result.StdOut}\n{result.StdErr}".Trim();
        Assert.True(result.ExitCode == 0, FormatFailureMessage("run", result, runArgs));

        Assert.DoesNotContain("Failed to unregister class Chrome_WidgetWin_0", combinedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(@"ui\gfx\win\window_impl.cc:124", combinedOutput, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var marker = Path.Combine(dir.FullName, "Agibuild.Fulora.slnx");
            if (File.Exists(marker))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (Agibuild.Fulora.slnx) from current directory.");
    }

    private static async Task<ProcessResult> RunProcessCaptureAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var timedOut = false;

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            timedOut = true;
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, stdout, stderr, timedOut, timeout);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore kill race/errors; exit state is validated by caller assertions.
        }
    }

    private static string FormatFailureMessage(string phase, ProcessResult result, string args)
    {
        var combinedOutput = $"{result.StdOut}\n{result.StdErr}".Trim();
        return
            $"WV2 teardown stress {phase} failed.\n" +
            $"Command: dotnet {args}\n" +
            $"ExitCode: {result.ExitCode}\n" +
            $"TimedOut: {result.TimedOut}\n" +
            $"Timeout: {result.Timeout}\n\n" +
            $"{combinedOutput}";
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, TimeSpan Timeout);
}

