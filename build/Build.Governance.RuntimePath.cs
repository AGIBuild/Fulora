using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    internal Target RuntimeCriticalPathExecutionGovernance => _ => _
        .Description("Validates runtime critical-path execution evidence.")
        .DependsOn(AutomationLaneReport, NugetPackageTest)
        .Executes(() =>
        {
            ValidateRuntimeCriticalPathExecutionEvidence();
        });

    private static void ValidateRuntimeCriticalPathExecutionEvidence()
    {
        TestResultsDirectory.CreateDirectory();
        if (!File.Exists(RuntimeCriticalPathManifestFile))
            Assert.Fail($"Missing runtime critical-path manifest: {RuntimeCriticalPathManifestFile}");

        var runtimeTrxPath = ResolveFirstExistingPath(
            TestResultsDirectory / "runtime-automation.trx",
            TestResultsDirectory / "integration-tests.trx");
        var contractTrxPath = ResolveFirstExistingPath(
            TestResultsDirectory / "contract-automation.trx",
            TestResultsDirectory / "unit-tests.trx");

        var runtimePassed = runtimeTrxPath is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : ReadPassedTestNamesFromTrx(runtimeTrxPath);
        var contractPassed = contractTrxPath is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : ReadPassedTestNamesFromTrx(contractTrxPath);

        using var manifestDoc = JsonDocument.Parse(File.ReadAllText(RuntimeCriticalPathManifestFile));
        var scenarios = manifestDoc.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();

        var failures = new List<string>();
        var checks = new List<object>();

        foreach (var scenario in scenarios)
        {
            var id = scenario.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            var lane = scenario.TryGetProperty("lane", out var laneNode) ? laneNode.GetString() : null;
            var file = scenario.TryGetProperty("file", out var fileNode) ? fileNode.GetString() : null;
            var testMethod = scenario.TryGetProperty("testMethod", out var methodNode) ? methodNode.GetString() : null;

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(lane))
            {
                failures.Add("Runtime critical-path scenario is missing required id/lane fields.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(file))
            {
                failures.Add($"Scenario '{id}' is missing required file field.");
                continue;
            }

            if (string.Equals(file, "build/Build.cs", StringComparison.Ordinal))
            {
                if (string.Equals(id, "package-consumption-smoke", StringComparison.Ordinal))
                {
                    var telemetryExists = File.Exists(NugetSmokeTelemetryFile);
                    var smokeTestAvailable = OperatingSystem.IsMacOS();
                    checks.Add(new
                    {
                        id,
                        lane,
                        evidenceType = "nuget-smoke-telemetry",
                        telemetryPath = NugetSmokeTelemetryFile.ToString(),
                        passed = telemetryExists,
                        skippedReason = smokeTestAvailable ? (string?)null : "GUI smoke test requires macOS display server"
                    });

                    if (smokeTestAvailable && !telemetryExists)
                        failures.Add($"Scenario '{id}' requires NuGet smoke telemetry evidence at '{NugetSmokeTelemetryFile}'.");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(testMethod))
            {
                failures.Add($"Scenario '{id}' must declare testMethod for test evidence validation.");
                continue;
            }

            HashSet<string>? passedTests = lane.StartsWith("RuntimeAutomation", StringComparison.Ordinal)
                ? runtimePassed
                : lane.StartsWith("ContractAutomation", StringComparison.Ordinal)
                    ? contractPassed
                    : null;

            if (passedTests is null)
            {
                failures.Add($"Scenario '{id}' has unsupported lane '{lane}'.");
                continue;
            }

            var passed = HasPassedTestMethod(passedTests, testMethod);
            checks.Add(new
            {
                id,
                lane,
                testMethod,
                passed
            });

            if (!passed)
                failures.Add($"Scenario '{id}' expected passed test evidence for method '{testMethod}' in lane '{lane}'.");
        }

        var reportPayload = new
        {
            schemaVersion = 2,
            provenance = new
            {
                laneContext = LaneContextCi,
                producerTarget = "RuntimeCriticalPathExecutionGovernance",
                timestamp = DateTime.UtcNow.ToString("o")
            },
            manifestPath = RuntimeCriticalPathManifestFile.ToString(),
            runtimeTrxPath = runtimeTrxPath?.ToString(),
            contractTrxPath = contractTrxPath?.ToString(),
            checks,
            failureCount = failures.Count,
            failures
        };

        WriteJsonReport(RuntimeCriticalPathGovernanceReportFile, reportPayload);
        Serilog.Log.Information("Runtime critical-path governance report written to {Path}", RuntimeCriticalPathGovernanceReportFile);

        if (failures.Count > 0)
            Assert.Fail("Runtime critical-path execution governance failed:\n" + string.Join('\n', failures));
    }
}
