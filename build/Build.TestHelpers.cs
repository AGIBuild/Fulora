using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

internal partial class BuildTask
{
    // Timeout budget notes:
    //   Observed wall time of Agibuild.Fulora.UnitTests on cold caches:
    //     - Linux runner   : ~4:00 (then the retry on warm caches finishes in <30s)
    //     - macOS runner   : ~4:10 to 4:30
    //     - Windows runner : ~4:10 to 4:30
    //   A 4-minute first-attempt budget landed exactly on the edge, so every CI run was
    //   consuming its retry and on slower runners both attempts timed out at 4:00 each
    //   (total 8:00) even though the suite was not actually hung. 7 minutes reflects the
    //   measured P99 with comfortable margin; it is NOT a defensive bump -- it tracks
    //   reality. If this budget is exhausted again the suite has genuinely regressed
    //   and needs sharding (xunit collections) rather than another timeout bump.
    private static readonly TimeSpan UnitTestRunTimeout = TimeSpan.FromMinutes(7);

    // Integration tests are a smaller suite (runtime lane only). 3 minutes has held.
    private static readonly TimeSpan IntegrationTestRunTimeout = TimeSpan.FromMinutes(3);

    private async Task RunContractAutomationTests(string trxFileName)
    {
        await CleanupLingeringUnitTestProcessesAsync();
        await RunDotNetTestWithHangRecoveryAsync(
            projectFile: UnitTestsProject,
            resultsDirectory: TestResultsDirectory,
            trxFileName: trxFileName,
            timeout: UnitTestRunTimeout,
            cleanupBetweenAttempts: CleanupLingeringUnitTestProcessesAsync);
    }

    private async Task RunRuntimeAutomationTests(string trxFileName)
    {
        await CleanupLingeringIntegrationAutomationProcessesAsync();
        await RunDotNetTestWithHangRecoveryAsync(
            projectFile: IntegrationTestsProject,
            resultsDirectory: TestResultsDirectory,
            trxFileName: trxFileName,
            timeout: IntegrationTestRunTimeout,
            cleanupBetweenAttempts: CleanupLingeringIntegrationAutomationProcessesAsync);
    }

    private async Task RunCoverageUnitTestsAsync(AbsolutePath resultsDirectory, string trxFileName)
    {
        await CleanupLingeringUnitTestProcessesAsync();
        await RunDotNetTestWithHangRecoveryAsync(
            projectFile: UnitTestsProject,
            resultsDirectory: resultsDirectory,
            trxFileName: trxFileName,
            timeout: UnitTestRunTimeout,
            settingsFile: RootDirectory / "coverlet.runsettings",
            timeoutMessageTemplate: "Coverage unit tests timed out on attempt {Attempt}. Retrying once after cleanup.",
            cleanupBetweenAttempts: CleanupLingeringUnitTestProcessesAsync);
    }

    private async Task RunDotNetTestWithHangRecoveryAsync(
        AbsolutePath projectFile,
        AbsolutePath resultsDirectory,
        string trxFileName,
        TimeSpan timeout,
        AbsolutePath? settingsFile = null,
        string timeoutMessageTemplate = "dotnet test timed out on attempt {Attempt}. Retrying once after cleanup.",
        Func<Task>? cleanupBetweenAttempts = null)
    {
        var arguments = new List<string>
        {
            "test",
            projectFile.ToString(),
            "--configuration", Configuration,
            "--no-restore",
            "--no-build",
            "--results-directory", resultsDirectory.ToString(),
            "--logger", $"trx;LogFileName={trxFileName}"
        };

        if (settingsFile is not null)
        {
            arguments.Add("--settings");
            arguments.Add(settingsFile.ToString());
        }

        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await RunProcessCheckedAsync(
                    "dotnet",
                    arguments.ToArray(),
                    workingDirectory: RootDirectory,
                    timeout: timeout);
                return;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                Serilog.Log.Warning(timeoutMessageTemplate, attempt);
                if (cleanupBetweenAttempts is not null)
                    await cleanupBetweenAttempts();
            }
        }
    }

    private async Task CleanupLingeringUnitTestProcessesAsync()
    {
        if (OperatingSystem.IsWindows())
            return;

        var testBinaryPath = $"{UnitTestsProject.Parent}/bin/{Configuration}/net10.0/Agibuild.Fulora.UnitTests";
        var testHostPath = $"{UnitTestsProject.Parent}/bin/{Configuration}/net10.0/testhost.dll";
        await CleanupProcessesByMarkerAsync(testBinaryPath);
        await CleanupProcessesByMarkerAsync(testHostPath);
    }

    private async Task CleanupLingeringIntegrationAutomationProcessesAsync()
    {
        if (OperatingSystem.IsWindows())
            return;

        var testBinaryPath = $"{IntegrationTestsProject.Parent}/bin/{Configuration}/net10.0/Agibuild.Fulora.Integration.Tests.Automation";
        var testHostPath = $"{IntegrationTestsProject.Parent}/bin/{Configuration}/net10.0/testhost.dll";
        await CleanupProcessesByMarkerAsync(testBinaryPath);
        await CleanupProcessesByMarkerAsync(testHostPath);
    }

    private static async Task CleanupProcessesByMarkerAsync(string marker)
    {
        try
        {
            var output = await RunProcessAsync("pkill", ["-f", marker], timeout: TimeSpan.FromSeconds(5));
            if (string.IsNullOrWhiteSpace(output))
            {
                Serilog.Log.Information("Cleared lingering test processes matching marker: {Marker}", marker);
            }
        }
        catch
        {
            // Best-effort cleanup. pkill returns non-zero when there are no matches.
        }
    }

    private async Task RunGtkSmokeDesktopAppAsync()
    {
        TestResultsDirectory.CreateDirectory();

        var output = await RunProcessCheckedAsync(
            "dotnet",
            ["run", "--project", E2EDesktopProject, "--configuration", Configuration, "--no-build", "--", "--gtk-smoke"],
            workingDirectory: RootDirectory,
            timeout: TimeSpan.FromMinutes(3));

        File.WriteAllText(TestResultsDirectory / "gtk-smoke.log", output);
    }

    private static void RunLaneWithReporting(
        string lane,
        AbsolutePath project,
        Action run,
        IList<AutomationLaneResult> lanes,
        IList<string> failures)
    {
        try
        {
            run();
            lanes.Add(new AutomationLaneResult(lane, "passed", project.ToString()));
        }
        catch (Exception ex)
        {
            var message = ex.Message.Split('\n').FirstOrDefault() ?? ex.Message;
            lanes.Add(new AutomationLaneResult(lane, "failed", project.ToString(), message));
            failures.Add($"{lane}: {message}");
        }
    }

    private static async Task RunLaneWithReportingAsync(
        string lane,
        AbsolutePath project,
        Func<Task> run,
        IList<AutomationLaneResult> lanes,
        List<string> failures)
    {
        try
        {
            await run();
            lanes.Add(new AutomationLaneResult(lane, "passed", project.ToString()));
        }
        catch (Exception ex)
        {
            var message = ex.Message.Split('\n').FirstOrDefault() ?? ex.Message;
            lanes.Add(new AutomationLaneResult(lane, "failed", project.ToString(), message));
            failures.Add($"{lane}: {message}");
        }
    }

    /// <summary>
    /// Records a lane result by reading a TRX file produced by an earlier target (e.g. Coverage)
    /// instead of re-executing the same test project. This avoids running an identical 2k-test
    /// suite twice in CI, which on slower runners (macOS) made the AutomationLaneReport target
    /// blow past its own dotnet-test timeout even though the underlying lane already passed in
    /// Coverage. The lane status reflects whatever the upstream TRX recorded.
    /// </summary>
    private static void RecordLaneFromTrx(
        string lane,
        AbsolutePath project,
        AbsolutePath trxFile,
        IList<AutomationLaneResult> lanes,
        IList<string> failures)
    {
        if (!File.Exists(trxFile))
        {
            const string Message = "Upstream TRX not found; lane status cannot be derived.";
            lanes.Add(new AutomationLaneResult(lane, "failed", project.ToString(), Message));
            failures.Add($"{lane}: {Message} ({trxFile})");
            return;
        }

        var counters = ReadTrxCounters(trxFile);
        var summary =
            $"derived from {trxFile.Name}: total={counters.Total}, passed={counters.Passed}, " +
            $"failed={counters.Failed}, skipped={counters.Skipped}";

        if (counters.Failed > 0 || counters.Total == 0)
        {
            lanes.Add(new AutomationLaneResult(lane, "failed", project.ToString(), summary));
            failures.Add($"{lane}: {summary}");
        }
        else
        {
            lanes.Add(new AutomationLaneResult(lane, "passed", project.ToString(), summary));
        }
    }

    private static (int Total, int Passed, int Failed, int Skipped) ReadTrxCounters(AbsolutePath trxPath)
    {
        var doc = XDocument.Load(trxPath);
        var counters = doc.Root?
            .Element(XName.Get("ResultSummary", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"))?
            .Element(XName.Get("Counters", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"));

        if (counters is null)
            Assert.Fail($"Unable to parse counters from TRX file: {trxPath}");

        static int ParseIntOrZero(XAttribute? attr) =>
            attr is null || !int.TryParse(attr.Value, out var parsed) ? 0 : parsed;

        return (
            Total: ParseIntOrZero(counters!.Attribute("total")),
            Passed: ParseIntOrZero(counters.Attribute("passed")),
            Failed: ParseIntOrZero(counters.Attribute("failed")),
            Skipped: ParseIntOrZero(counters.Attribute("notExecuted")));
    }

    private static HashSet<string> ReadPassedTestNamesFromTrx(AbsolutePath trxPath)
    {
        var doc = XDocument.Load(trxPath);
        var ns = XNamespace.Get("http://microsoft.com/schemas/VisualStudio/TeamTest/2010");
        return doc
            .Descendants(ns + "UnitTestResult")
            .Where(result => string.Equals(
                result.Attribute("outcome")?.Value,
                "Passed",
                StringComparison.OrdinalIgnoreCase))
            .Select(result => result.Attribute("testName")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasPassedTestMethod(HashSet<string> passedTests, string testMethod)
    {
        return passedTests.Any(name =>
            name.Equals(testMethod, StringComparison.Ordinal)
            || name.EndsWith("." + testMethod, StringComparison.Ordinal)
            || name.Contains(testMethod, StringComparison.Ordinal));
    }

    private static double ReadCoberturaLineCoveragePercent(AbsolutePath coberturaPath)
    {
        var doc = XDocument.Load(coberturaPath);
        var lineRateAttr = doc.Root?.Attribute("line-rate")?.Value;

        if (lineRateAttr is null || !double.TryParse(
                lineRateAttr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var lineRate))
        {
            Assert.Fail($"Unable to parse line-rate from coverage report: {coberturaPath}");
            return 0;
        }

        return lineRate * 100;
    }

    private static double ReadCoberturaBranchCoveragePercent(AbsolutePath coberturaPath)
    {
        var doc = XDocument.Load(coberturaPath);
        var branchRateAttr = doc.Root?.Attribute("branch-rate")?.Value;

        if (branchRateAttr is null || !double.TryParse(
                branchRateAttr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var branchRate))
        {
            Assert.Fail($"Unable to parse branch-rate from coverage report: {coberturaPath}");
            return 0;
        }

        return branchRate * 100;
    }
}
