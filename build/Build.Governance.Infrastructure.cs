using System;
using System.Collections.Generic;
using System.Linq;
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
        WriteGovernanceReport(reportFile, result.ReportPayload);
        Serilog.Log.Information("{Target} report written to {Path}", targetName, reportFile);

        if (result.Failures.Count > 0)
        {
            var lines = result.Failures.Select(FormatGovernanceFailure);
            Assert.Fail($"{targetName} failed:\n{string.Join('\n', lines)}");
        }
    }

    private static async System.Threading.Tasks.Task RunGovernanceCheckAsync(
        string targetName,
        AbsolutePath reportFile,
        Func<System.Threading.Tasks.Task<GovernanceCheckResult>> executeChecks)
    {
        TestResultsDirectory.CreateDirectory();
        var result = await executeChecks();
        WriteGovernanceReport(reportFile, result.ReportPayload);
        Serilog.Log.Information("{Target} report written to {Path}", targetName, reportFile);

        if (result.Failures.Count > 0)
        {
            var lines = result.Failures.Select(FormatGovernanceFailure);
            Assert.Fail($"{targetName} failed:\n{string.Join('\n', lines)}");
        }
    }

    private static string FormatGovernanceFailure(GovernanceFailure f) =>
        $"[{f.InvariantId}] {f.SourceArtifact}: expected {f.Expected}, actual {f.Actual}";

    private sealed record GovernanceCheckResult(
        IReadOnlyList<GovernanceFailure> Failures,
        object ReportPayload);
}
