using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private const string RuntimeCriticalPathInvariantId = "GOV-041";

    internal Target RuntimeCriticalPathExecutionGovernance => _ => _
        .Description("Validates runtime critical-path execution evidence.")
        .DependsOn(AutomationLaneReport, NugetPackageTest)
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Runtime critical-path execution governance",
                RuntimeCriticalPathGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();
                    var checks = new List<object>();

                    if (!File.Exists(RuntimeCriticalPathManifestFile))
                    {
                        failures.Add(new GovernanceFailure(
                            Category: "runtime-critical-path",
                            InvariantId: RuntimeCriticalPathInvariantId,
                            SourceArtifact: RuntimeCriticalPathManifestFile.ToString(),
                            Expected: "runtime critical-path manifest exists",
                            Actual: "missing"));

                        var earlyPayload = new
                        {
                            schemaVersion = 2,
                            provenance = new
                            {
                                laneContext = LaneContextCi,
                                producerTarget = "RuntimeCriticalPathExecutionGovernance",
                                timestamp = DateTime.UtcNow.ToString("o")
                            },
                            manifestPath = RuntimeCriticalPathManifestFile.ToString(),
                            checks,
                            failureCount = failures.Count,
                            failures
                        };
                        return new GovernanceCheckResult(failures, earlyPayload);
                    }

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

                    foreach (var scenario in scenarios)
                    {
                        var id = scenario.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
                        var lane = scenario.TryGetProperty("lane", out var laneNode) ? laneNode.GetString() : null;
                        var file = scenario.TryGetProperty("file", out var fileNode) ? fileNode.GetString() : null;
                        var testMethod = scenario.TryGetProperty("testMethod", out var methodNode) ? methodNode.GetString() : null;

                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(lane))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "runtime-critical-path",
                                InvariantId: RuntimeCriticalPathInvariantId,
                                SourceArtifact: RuntimeCriticalPathManifestFile.ToString(),
                                Expected: "scenario has id and lane fields",
                                Actual: "missing required fields"));
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(file))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "runtime-critical-path",
                                InvariantId: RuntimeCriticalPathInvariantId,
                                SourceArtifact: $"scenario '{id}'",
                                Expected: "file field present",
                                Actual: "missing"));
                            continue;
                        }

                        if (string.Equals(file, "build/Build.cs", StringComparison.Ordinal))
                        {
                            if (string.Equals(id, "package-consumption-smoke", StringComparison.Ordinal))
                            {
                                var telemetryExists = File.Exists(NugetSmokeTelemetryFile);
                                var smokeTestAvailable = IsMacOsGuiSmokeEnvironment();
                                checks.Add(new
                                {
                                    id,
                                    lane,
                                    evidenceType = "nuget-smoke-telemetry",
                                    telemetryPath = NugetSmokeTelemetryFile.ToString(),
                                    passed = telemetryExists,
                                    skippedReason = smokeTestAvailable ? (string?)null : "GUI smoke test runs only on macOS CI agents with a display server"
                                });

                                if (smokeTestAvailable && !telemetryExists)
                                    failures.Add(new GovernanceFailure(
                                        Category: "runtime-critical-path",
                                        InvariantId: RuntimeCriticalPathInvariantId,
                                        SourceArtifact: NugetSmokeTelemetryFile.ToString(),
                                        Expected: "NuGet smoke telemetry evidence exists",
                                        Actual: "missing"));
                            }

                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(testMethod))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "runtime-critical-path",
                                InvariantId: RuntimeCriticalPathInvariantId,
                                SourceArtifact: $"scenario '{id}'",
                                Expected: "testMethod declared for test evidence validation",
                                Actual: "missing"));
                            continue;
                        }

                        HashSet<string>? passedTests = lane.StartsWith("RuntimeAutomation", StringComparison.Ordinal)
                            ? runtimePassed
                            : lane.StartsWith("ContractAutomation", StringComparison.Ordinal)
                                ? contractPassed
                                : null;

                        if (passedTests is null)
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "runtime-critical-path",
                                InvariantId: RuntimeCriticalPathInvariantId,
                                SourceArtifact: $"scenario '{id}'",
                                Expected: "supported lane (RuntimeAutomation or ContractAutomation)",
                                Actual: $"unsupported lane '{lane}'"));
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
                            failures.Add(new GovernanceFailure(
                                Category: "runtime-critical-path",
                                InvariantId: RuntimeCriticalPathInvariantId,
                                SourceArtifact: $"scenario '{id}' ({lane})",
                                Expected: $"passed test evidence for '{testMethod}'",
                                Actual: "test not passed"));
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

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });
}
