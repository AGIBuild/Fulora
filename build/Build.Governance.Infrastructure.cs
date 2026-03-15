using System;
using System.Collections.Generic;
using System.Text.Json;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private sealed record GovernanceFailure(
        string Category,
        string InvariantId,
        string SourceArtifact,
        string Expected,
        string Actual);

    private static void RunGovernanceCheck(
        string targetName,
        AbsolutePath reportFile,
        Func<GovernanceCheckResult> executeChecks)
    {
        TestResultsDirectory.CreateDirectory();
        var result = executeChecks();
        WriteJsonReport(reportFile, result.ReportPayload);
        Serilog.Log.Information("{Target} report written to {Path}", targetName, reportFile);

        if (result.Failures.Count > 0)
        {
            var lines = string.Join('\n', result.Failures);
            Assert.Fail($"{targetName} failed:\n{lines}");
        }
    }

    private static async System.Threading.Tasks.Task RunGovernanceCheckAsync(
        string targetName,
        AbsolutePath reportFile,
        Func<System.Threading.Tasks.Task<GovernanceCheckResult>> executeChecks)
    {
        TestResultsDirectory.CreateDirectory();
        var result = await executeChecks();
        WriteJsonReport(reportFile, result.ReportPayload);
        Serilog.Log.Information("{Target} report written to {Path}", targetName, reportFile);

        if (result.Failures.Count > 0)
        {
            var lines = string.Join('\n', result.Failures);
            Assert.Fail($"{targetName} failed:\n{lines}");
        }
    }

    private sealed record GovernanceCheckResult(
        IReadOnlyList<string> Failures,
        object ReportPayload);
}
