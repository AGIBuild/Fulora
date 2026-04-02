using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nuke.Common;

internal partial class BuildTask
{
    private const string DependencyVulnerabilityInvariantId = "GOV-037";

    internal Target DependencyVulnerabilityGovernance => _ => _
        .Description("Runs dependency vulnerability scans (NuGet + npm) as a hard governance gate.")
        .Executes(async () =>
        {
            await RunGovernanceCheckAsync(
                "Dependency vulnerability governance",
                DependencyGovernanceReportFile,
                async () =>
                {
                    var failures = new List<GovernanceFailure>();
                    var scanReports = new List<object>();

                    var filterPath = await BuildPlatformAwareSolutionFilterAsync("vuln-scan");
                    var nugetOutput = await RunProcessCaptureAllAsync(
                        "dotnet",
                        ["list", (string)filterPath, "package", "--vulnerable", "--include-transitive"],
                        workingDirectory: RootDirectory,
                        timeout: TimeSpan.FromMinutes(4));
                    var nugetHasVulnerability = nugetOutput.Contains("has the following vulnerable packages", StringComparison.OrdinalIgnoreCase)
                                                || nugetOutput.Contains("vulnerable", StringComparison.OrdinalIgnoreCase)
                                                && nugetOutput.Contains("Severity", StringComparison.OrdinalIgnoreCase);
                    scanReports.Add(new
                    {
                        ecosystem = "nuget",
                        command = "dotnet list <solution> package --vulnerable --include-transitive",
                        hasFindings = nugetHasVulnerability,
                        output = nugetOutput
                    });
                    if (nugetHasVulnerability)
                        failures.Add(new GovernanceFailure(
                            Category: "dependency-vulnerability",
                            InvariantId: DependencyVulnerabilityInvariantId,
                            SourceArtifact: "dotnet list package --vulnerable",
                            Expected: "no vulnerable packages",
                            Actual: "vulnerable packages detected"));

                    var npmWorkspaces = new[]
                    {
                        ReactWebDirectory,
                        AiChatWebDirectory,
                        VueWebDirectory,
                        TodoWebDirectory
                    }
                    .Distinct()
                    .ToArray();

                    foreach (var workspace in npmWorkspaces)
                    {
                        if (!File.Exists(workspace / "package-lock.json"))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "dependency-vulnerability",
                                InvariantId: DependencyVulnerabilityInvariantId,
                                SourceArtifact: workspace.ToString(),
                                Expected: "missing npm lockfile is treated as a governance failure",
                                Actual: "missing npm lockfile"));
                            continue;
                        }

                        var npmOutput = await RunNpmStdoutAsync(
                            ["audit", "--json", "--audit-level=high"],
                            workspace,
                            TimeSpan.FromMinutes(3));

                        var hasHighOrCritical = false;
                        try
                        {
                            using var doc = JsonDocument.Parse(npmOutput);
                            if (doc.RootElement.TryGetProperty("metadata", out var metadata)
                                && metadata.TryGetProperty("vulnerabilities", out var vulnerabilities))
                            {
                                var high = vulnerabilities.TryGetProperty("high", out var highNode) ? highNode.GetInt32() : 0;
                                var critical = vulnerabilities.TryGetProperty("critical", out var criticalNode) ? criticalNode.GetInt32() : 0;
                                hasHighOrCritical = high > 0 || critical > 0;
                            }
                        }
                        catch (JsonException)
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "dependency-vulnerability",
                                InvariantId: DependencyVulnerabilityInvariantId,
                                SourceArtifact: workspace.ToString(),
                                Expected: "valid JSON audit output",
                                Actual: "npm audit output is not valid JSON"));
                        }

                        scanReports.Add(new
                        {
                            ecosystem = "npm",
                            workspace = workspace.ToString(),
                            command = "npm audit --json --audit-level=high",
                            hasFindings = hasHighOrCritical
                        });

                        if (hasHighOrCritical)
                            failures.Add(new GovernanceFailure(
                                Category: "dependency-vulnerability",
                                InvariantId: DependencyVulnerabilityInvariantId,
                                SourceArtifact: workspace.ToString(),
                                Expected: "no high/critical vulnerabilities",
                                Actual: "high/critical vulnerabilities detected"));
                    }

                    var reportPayload = new
                    {
                        generatedAtUtc = DateTime.UtcNow,
                        scans = scanReports,
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
