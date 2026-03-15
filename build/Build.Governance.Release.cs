using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;

internal partial class BuildTask
{
    private const string TransitionGateParityInvariantId = "GOV-024";
    private const string TransitionLaneProvenanceInvariantId = "GOV-025";
    private const string TransitionGateDiagnosticSchemaInvariantId = "GOV-026";
    private const string ReleaseOrchestrationDecisionInvariantId = "GOV-027";
    private const string ReleaseOrchestrationBlockingReasonSchemaInvariantId = "GOV-028";
    private const string StablePublishReadinessInvariantId = "GOV-029";
    private const string DistributionReadinessDecisionInvariantId = "GOV-030";
    private const string DistributionReadinessSchemaInvariantId = "GOV-031";
    private const string AdoptionReadinessSchemaInvariantId = "GOV-032";
    private const string AdoptionReadinessPolicyInvariantId = "GOV-033";
    private const string ReleaseEvidenceReadinessSectionsInvariantId = "GOV-034";
    private const string BridgeSingleEntryAppLayerPolicyInvariantId = "GOV-035";
    private const string SampleTemplatePackageReferencePolicyInvariantId = "GOV-036";

    private sealed record TransitionGateParityRule(string Group, string CiDependency, string CiPublishDependency);

    private sealed record TransitionGateDiagnosticEntry(
        string InvariantId,
        string Lane,
        string ArtifactPath,
        string Expected,
        string Actual,
        string Group);

    private static readonly string[] CompletedPhaseCloseoutChangeIds =
    [
        "sentry-crash-reporting",
        "shared-state-management",
        "enterprise-auth-patterns",
        "plugin-quality-compatibility"
    ];

    private static readonly TransitionGateParityRule[] CloseoutCriticalTransitionGateParityRules =
    [
        new("coverage", "Coverage", "Coverage"),
        new("automation-lane-report", "AutomationLaneReport", "AutomationLaneReport"),
        new("warning-governance", "WarningGovernance", "WarningGovernance"),
        new("dependency-vulnerability-governance", "DependencyVulnerabilityGovernance", "DependencyVulnerabilityGovernance"),
        new("typescript-declaration-governance", "TypeScriptDeclarationGovernance", "TypeScriptDeclarationGovernance"),
        new("openspec-strict-governance", "OpenSpecStrictGovernance", "OpenSpecStrictGovernance"),
        new("release-closeout-snapshot", "ReleaseCloseoutSnapshot", "ReleaseCloseoutSnapshot"),
        new("runtime-critical-path-governance", "RuntimeCriticalPathExecutionGovernance", "RuntimeCriticalPathExecutionGovernance"),
        new("adoption-readiness-governance", "AdoptionReadinessGovernance", "AdoptionReadinessGovernance")
    ];

    internal Target ContinuousTransitionGateGovernance => _ => _
        .Description("Validates closeout transition-gate governance targets are present in Ci with lane-aware diagnostics.")
        .DependsOn(ReleaseCloseoutSnapshot)
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();

            var diagnostics = new List<TransitionGateDiagnosticEntry>();
            var failures = new List<string>();
            const string buildArtifactPath = "build/Build.cs";

            var buildSource = File.ReadAllText(RootDirectory / "build" / "Build.cs");
            var ciDependsOnBlock = ExtractDependsOnBlock(buildSource, LaneContextCi);
            var ciPublishDependsOnBlock = ExtractDependsOnBlock(buildSource, LaneContextCiPublish);

            foreach (var rule in CloseoutCriticalTransitionGateParityRules)
            {
                if (!ciDependsOnBlock.Contains(rule.CiDependency, StringComparison.Ordinal))
                {
                    diagnostics.Add(new TransitionGateDiagnosticEntry(
                        TransitionGateParityInvariantId,
                        Lane: LaneContextCi,
                        ArtifactPath: buildArtifactPath,
                        Expected: rule.CiDependency,
                        Actual: "missing",
                        Group: rule.Group));
                    failures.Add($"[{TransitionGateParityInvariantId}] Missing Ci dependency '{rule.CiDependency}' for group '{rule.Group}'.");
                }
            }

            if (!ciPublishDependsOnBlock.Contains(LaneContextCi, StringComparison.Ordinal))
            {
                diagnostics.Add(new TransitionGateDiagnosticEntry(
                    TransitionGateParityInvariantId,
                    Lane: LaneContextCiPublish,
                    ArtifactPath: buildArtifactPath,
                    Expected: LaneContextCi,
                    Actual: "missing",
                    Group: "ci-inheritance"));
                failures.Add($"[{TransitionGateParityInvariantId}] CiPublish must depend on Ci.");
            }

            var roadmapPath = RootDirectory / "openspec" / "ROADMAP.md";
            var (roadmapCompletedPhase, roadmapActivePhase) = ReadRoadmapTransitionState(File.ReadAllText(roadmapPath));
            const string closeoutArtifactPath = "artifacts/test-results/closeout-snapshot.json";

            if (!File.Exists(CloseoutSnapshotFile))
            {
                diagnostics.Add(new TransitionGateDiagnosticEntry(
                    TransitionLaneProvenanceInvariantId,
                    Lane: LaneContextCi,
                    ArtifactPath: closeoutArtifactPath,
                    Expected: "closeout snapshot exists",
                    Actual: "file missing",
                    Group: "transition-continuity"));
                failures.Add($"[{TransitionLaneProvenanceInvariantId}] Closeout snapshot file missing at '{CloseoutSnapshotFile}'.");
            }
            else
            {
                using var closeoutDoc = JsonDocument.Parse(File.ReadAllText(CloseoutSnapshotFile));
                var root = closeoutDoc.RootElement;
                var provenance = root.GetProperty("provenance");
                var transition = root.GetProperty("transition");
                var continuity = root.GetProperty("transitionContinuity");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: LaneContextCi,
                    actual: provenance.GetProperty("laneContext").GetString(),
                    group: "transition-continuity",
                    fieldName: "provenance.laneContext");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: "ReleaseCloseoutSnapshot",
                    actual: provenance.GetProperty("producerTarget").GetString(),
                    group: "transition-continuity",
                    fieldName: "provenance.producerTarget");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: roadmapCompletedPhase,
                    actual: transition.GetProperty("completedPhase").GetString(),
                    group: "transition-continuity",
                    fieldName: "transition.completedPhase");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: roadmapActivePhase,
                    actual: transition.GetProperty("activePhase").GetString(),
                    group: "transition-continuity",
                    fieldName: "transition.activePhase");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: LaneContextCi,
                    actual: continuity.GetProperty("laneContext").GetString(),
                    group: "transition-continuity",
                    fieldName: "transitionContinuity.laneContext");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: "ReleaseCloseoutSnapshot",
                    actual: continuity.GetProperty("producerTarget").GetString(),
                    group: "transition-continuity",
                    fieldName: "transitionContinuity.producerTarget");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: roadmapCompletedPhase,
                    actual: continuity.GetProperty("completedPhase").GetString(),
                    group: "transition-continuity",
                    fieldName: "transitionContinuity.completedPhase");

                ValidateTransitionField(
                    diagnostics,
                    failures,
                    lane: LaneContextCi,
                    artifactPath: closeoutArtifactPath,
                    expected: roadmapActivePhase,
                    actual: continuity.GetProperty("activePhase").GetString(),
                    group: "transition-continuity",
                    fieldName: "transitionContinuity.activePhase");
            }

            var reportPayload = new
            {
                schemaVersion = 1,
                generatedAtUtc = DateTime.UtcNow,
                parityRules = CloseoutCriticalTransitionGateParityRules.Select(rule => new
                {
                    group = rule.Group,
                    ciDependency = rule.CiDependency,
                    ciPublishDependency = rule.CiPublishDependency
                }),
                diagnostics = diagnostics.Select(x => new
                {
                    invariantId = x.InvariantId,
                    lane = x.Lane,
                    artifactPath = x.ArtifactPath,
                    expected = x.Expected,
                    actual = x.Actual,
                    group = x.Group
                }),
                failureCount = failures.Count,
                failures
            };

            WriteJsonReport(TransitionGateGovernanceReportFile, reportPayload);
            Serilog.Log.Information("Transition gate governance report written to {Path}", TransitionGateGovernanceReportFile);

            if (failures.Count > 0)
                Assert.Fail("Continuous transition gate governance failed:\n" + string.Join('\n', failures));
        });

    private static string ExtractDependsOnBlock(string buildSource, string targetName)
    {
        var match = Regex.Match(
            buildSource,
            $@"Target\s+{Regex.Escape(targetName)}\s*=>[\s\S]*?\.DependsOn\((?<deps>[\s\S]*?)\);",
            RegexOptions.Multiline);
        if (!match.Success)
            Assert.Fail($"Unable to locate DependsOn block for target '{targetName}' in build/Build.cs.");

        return match.Groups["deps"].Value;
    }

    private static (string CompletedPhase, string ActivePhase) ReadRoadmapTransitionState(string roadmap)
    {
        var completedMatch = Regex.Match(roadmap, @"Completed phase id:\s*`(?<id>[^`]+)`", RegexOptions.Multiline);
        var activeMatch = Regex.Match(roadmap, @"Active phase id:\s*`(?<id>[^`]+)`", RegexOptions.Multiline);
        if (!completedMatch.Success || !activeMatch.Success)
            Assert.Fail("ROADMAP transition markers are missing machine-checkable phase ids.");

        return (
            completedMatch.Groups["id"].Value.Trim(),
            activeMatch.Groups["id"].Value.Trim());
    }

    private static void ValidateTransitionField(
        IList<TransitionGateDiagnosticEntry> diagnostics,
        List<string> failures,
        string lane,
        string artifactPath,
        string expected,
        string? actual,
        string group,
        string fieldName)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        diagnostics.Add(new TransitionGateDiagnosticEntry(
            TransitionLaneProvenanceInvariantId,
            lane,
            artifactPath,
            Expected: $"{fieldName} = {expected}",
            Actual: $"{fieldName} = {actual ?? "<null>"}",
            Group: group));
        failures.Add($"[{TransitionLaneProvenanceInvariantId}] Transition continuity mismatch for {fieldName}. Expected '{expected}', actual '{actual ?? "<null>"}'.");
    }

    internal Target ReleaseCloseoutSnapshot => _ => _
        .Description("Generates machine-readable CI evidence snapshot (v2) from test/coverage artifacts.")
        .DependsOn(Coverage, AutomationLaneReport, OpenSpecStrictGovernance)
        .Executes(() =>
        {
            const string completedPhase = "phase12-enterprise-advanced-scenarios";
            const string activePhase = "post-roadmap-maintenance";
            const string transitionInvariantId = "GOV-022";
            var roadmapPath = RootDirectory / "openspec" / "ROADMAP.md";
            var (roadmapCompletedPhase, roadmapActivePhase) = ReadRoadmapTransitionState(File.ReadAllText(roadmapPath));
            if (!string.Equals(roadmapCompletedPhase, completedPhase, StringComparison.Ordinal)
                || !string.Equals(roadmapActivePhase, activePhase, StringComparison.Ordinal))
            {
                Assert.Fail(
                    $"[{TransitionLaneProvenanceInvariantId}] Closeout snapshot transition constants drifted from ROADMAP markers. " +
                    $"Expected ({roadmapCompletedPhase}, {roadmapActivePhase}), actual ({completedPhase}, {activePhase}).");
            }

            TestResultsDirectory.CreateDirectory();

            var unitTrxPath = ResolveFirstExistingPath(
                TestResultsDirectory / "unit-tests.trx",
                CoverageDirectory / "unit-tests.trx");
            var integrationTrxPath = ResolveFirstExistingPath(
                TestResultsDirectory / "integration-tests.trx",
                TestResultsDirectory / "runtime-automation.trx");
            var coberturaPath = ResolveFirstExistingPath(
                CoverageReportDirectory / "Cobertura.xml",
                CoverageDirectory.GlobFiles("**/coverage.cobertura.xml").FirstOrDefault());

            if (unitTrxPath is null)
                Assert.Fail("CI evidence snapshot requires unit test TRX file (unit-tests.trx).");
            if (integrationTrxPath is null)
                Assert.Fail("CI evidence snapshot requires integration/runtime automation TRX file.");
            if (coberturaPath is null)
                Assert.Fail("CI evidence snapshot requires Cobertura coverage report.");

            var unitCounters = ReadTrxCounters(unitTrxPath!);
            var integrationCounters = ReadTrxCounters(integrationTrxPath!);
            var lineCoveragePct = ReadCoberturaLineCoveragePercent(coberturaPath!);
            var branchCoveragePct = ReadCoberturaBranchCoveragePercent(coberturaPath!);

            var archiveDirectory = RootDirectory / "openspec" / "changes" / "archive";
            var closeoutArchives = Directory.Exists(archiveDirectory)
                ? CompletedPhaseCloseoutChangeIds
                    .Select(changeId => Directory.GetDirectories(archiveDirectory)
                        .Select(Path.GetFileName)
                        .FirstOrDefault(name => name is not null && name.EndsWith(changeId, StringComparison.Ordinal)))
                    .Where(name => name is not null)
                    .Cast<string>()
                    .ToArray()
                : Array.Empty<string>();

            var snapshotPayload = new
            {
                schemaVersion = 2,
                provenance = new
                {
                    laneContext = LaneContextCi,
                    producerTarget = "ReleaseCloseoutSnapshot",
                    timestamp = DateTime.UtcNow.ToString("o")
                },
                transition = new
                {
                    invariantId = transitionInvariantId,
                    completedPhase,
                    activePhase
                },
                transitionContinuity = new
                {
                    invariantId = TransitionLaneProvenanceInvariantId,
                    laneContext = LaneContextCi,
                    producerTarget = "ReleaseCloseoutSnapshot",
                    completedPhase,
                    activePhase
                },
                sourcePaths = new
                {
                    unitTrx = unitTrxPath!.ToString(),
                    integrationTrx = integrationTrxPath!.ToString(),
                    cobertura = coberturaPath!.ToString(),
                    openSpecStrictGovernance = OpenSpecStrictGovernanceReportFile.ToString()
                },
                tests = new
                {
                    unit = new
                    {
                        total = unitCounters.Total,
                        passed = unitCounters.Passed,
                        failed = unitCounters.Failed,
                        skipped = unitCounters.Skipped
                    },
                    integration = new
                    {
                        total = integrationCounters.Total,
                        passed = integrationCounters.Passed,
                        failed = integrationCounters.Failed,
                        skipped = integrationCounters.Skipped
                    },
                    total = new
                    {
                        total = unitCounters.Total + integrationCounters.Total,
                        passed = unitCounters.Passed + integrationCounters.Passed,
                        failed = unitCounters.Failed + integrationCounters.Failed,
                        skipped = unitCounters.Skipped + integrationCounters.Skipped
                    }
                },
                coverage = new
                {
                    linePercent = Math.Round(lineCoveragePct, 2),
                    lineThreshold = CoverageThreshold,
                    branchPercent = Math.Round(branchCoveragePct, 2),
                    branchThreshold = BranchCoverageThreshold
                },
                governance = new
                {
                    openSpecStrictGovernanceReportExists = File.Exists(OpenSpecStrictGovernanceReportFile),
                    automationLaneReportExists = File.Exists(AutomationLaneReportFile),
                    dependencyGovernanceReportExists = File.Exists(DependencyGovernanceReportFile),
                    typeScriptGovernanceReportExists = File.Exists(TypeScriptGovernanceReportFile),
                    sampleTemplatePackageReferenceGovernanceReportExists = File.Exists(SampleTemplatePackageReferenceGovernanceReportFile),
                    runtimeCriticalPathGovernanceReportExists = File.Exists(RuntimeCriticalPathGovernanceReportFile)
                },
                closeoutArchives
            };

            WriteJsonReport(CloseoutSnapshotFile, snapshotPayload);
            Serilog.Log.Information("CI evidence snapshot (v2) written to {Path}", CloseoutSnapshotFile);
        });

    internal Target ReleaseOrchestrationGovernance => _ => _
        .Description("Evaluates release-orchestration readiness and blocks publish side-effects when not ready.")
        .DependsOn(
            ReleaseCloseoutSnapshot,
            ContinuousTransitionGateGovernance,
            RuntimeCriticalPathExecutionGovernance,
            WarningGovernance,
            DependencyVulnerabilityGovernance,
            TypeScriptDeclarationGovernance,
            OpenSpecStrictGovernance,
            SampleTemplatePackageReferenceGovernance,
            BridgeDistributionGovernance,
            DistributionReadinessGovernance,
            AdoptionReadinessGovernance,
            ValidatePackage)
        .Executes(() =>
        {
            TestResultsDirectory.CreateDirectory();

            var blockingReasons = new List<GovernanceFailure>();
            void AddBlockingReason(string category, string invariantId, string sourceArtifact, string expected, string actual)
                => blockingReasons.Add(new GovernanceFailure(category, invariantId, sourceArtifact, expected, actual));

            var requiredArtifacts = new[]
            {
                new { Category = "evidence", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/closeout-snapshot.json", FullPath = CloseoutSnapshotFile.ToString() },
                new { Category = "governance", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/transition-gate-governance-report.json", FullPath = TransitionGateGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/dependency-governance-report.json", FullPath = DependencyGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/typescript-governance-report.json", FullPath = TypeScriptGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = SampleTemplatePackageReferencePolicyInvariantId, RelativePath = "artifacts/test-results/sample-template-package-reference-governance-report.json", FullPath = SampleTemplatePackageReferenceGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/runtime-critical-path-governance-report.json", FullPath = RuntimeCriticalPathGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = ReleaseOrchestrationDecisionInvariantId, RelativePath = "artifacts/test-results/bridge-distribution-governance-report.json", FullPath = BridgeDistributionGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = DistributionReadinessSchemaInvariantId, RelativePath = "artifacts/test-results/distribution-readiness-governance-report.json", FullPath = DistributionReadinessGovernanceReportFile.ToString() },
                new { Category = "governance", InvariantId = AdoptionReadinessSchemaInvariantId, RelativePath = "artifacts/test-results/adoption-readiness-governance-report.json", FullPath = AdoptionReadinessGovernanceReportFile.ToString() }
            };

            foreach (var artifact in requiredArtifacts)
            {
                if (!File.Exists(artifact.FullPath))
                {
                    AddBlockingReason(
                        category: artifact.Category,
                        invariantId: artifact.InvariantId,
                        sourceArtifact: artifact.RelativePath,
                        expected: "artifact exists",
                        actual: "missing");
                }
            }

            if (File.Exists(CloseoutSnapshotFile))
            {
                using var snapshotDoc = JsonDocument.Parse(File.ReadAllText(CloseoutSnapshotFile));
                var root = snapshotDoc.RootElement;

                if (root.TryGetProperty("coverage", out var coverage))
                {
                    var linePercent = coverage.GetProperty("linePercent").GetDouble();
                    var lineThreshold = coverage.GetProperty("lineThreshold").GetInt32();
                    var branchPercent = coverage.GetProperty("branchPercent").GetDouble();
                    var branchThreshold = coverage.GetProperty("branchThreshold").GetInt32();

                    if (linePercent < lineThreshold)
                    {
                        AddBlockingReason(
                            category: "quality-threshold",
                            invariantId: ReleaseOrchestrationDecisionInvariantId,
                            sourceArtifact: "artifacts/test-results/closeout-snapshot.json",
                            expected: $"linePercent >= {lineThreshold}",
                            actual: $"linePercent = {linePercent:F2}");
                    }

                    if (branchPercent < branchThreshold)
                    {
                        AddBlockingReason(
                            category: "quality-threshold",
                            invariantId: ReleaseOrchestrationDecisionInvariantId,
                            sourceArtifact: "artifacts/test-results/closeout-snapshot.json",
                            expected: $"branchPercent >= {branchThreshold}",
                            actual: $"branchPercent = {branchPercent:F2}");
                    }
                }
                else
                {
                    AddBlockingReason(
                        category: "evidence",
                        invariantId: ReleaseOrchestrationBlockingReasonSchemaInvariantId,
                        sourceArtifact: "artifacts/test-results/closeout-snapshot.json",
                        expected: "coverage section present",
                        actual: "coverage section missing");
                }

                if (!root.TryGetProperty("governance", out JsonElement governanceSection))
                {
                    AddBlockingReason(
                        category: "governance",
                        invariantId: ReleaseOrchestrationBlockingReasonSchemaInvariantId,
                        sourceArtifact: "artifacts/test-results/closeout-snapshot.json",
                        expected: "governance section present",
                        actual: "governance section missing");
                }
            }

            if (File.Exists(TransitionGateGovernanceReportFile))
            {
                using var transitionDoc = JsonDocument.Parse(File.ReadAllText(TransitionGateGovernanceReportFile));
                var failureCount = transitionDoc.RootElement.TryGetProperty("failureCount", out var failureNode)
                    ? failureNode.GetInt32()
                    : 0;

                if (failureCount > 0)
                {
                    AddBlockingReason(
                        category: "governance",
                        invariantId: TransitionGateParityInvariantId,
                        sourceArtifact: "artifacts/test-results/transition-gate-governance-report.json",
                        expected: "failureCount = 0",
                        actual: $"failureCount = {failureCount}");
                }
            }

            var distributionSummaryObject = new JsonObject
            {
                ["state"] = "unknown",
                ["isStableRelease"] = false,
                ["version"] = "unknown",
                ["failureCount"] = -1,
                ["sourceArtifact"] = "artifacts/test-results/distribution-readiness-governance-report.json"
            };
            var distributionFailureArray = new JsonArray();
            if (File.Exists(DistributionReadinessGovernanceReportFile))
            {
                var distributionNode = JsonNode.Parse(File.ReadAllText(DistributionReadinessGovernanceReportFile))?.AsObject()
                    ?? new JsonObject();
                var summaryNode = distributionNode["summary"] as JsonObject;
                if (summaryNode is null)
                {
                    AddBlockingReason(
                        category: "evidence",
                        invariantId: DistributionReadinessSchemaInvariantId,
                        sourceArtifact: "artifacts/test-results/distribution-readiness-governance-report.json",
                        expected: "summary section present",
                        actual: "summary section missing");
                }
                else
                {
                    var state = summaryNode["state"]?.GetValue<string>() ?? "unknown";
                    var isStable = summaryNode["isStableRelease"]?.GetValue<bool>() ?? false;
                    var version = summaryNode["version"]?.GetValue<string>() ?? "unknown";
                    var failureCount = summaryNode["failureCount"]?.GetValue<int>() ?? -1;

                    distributionSummaryObject["state"] = state;
                    distributionSummaryObject["isStableRelease"] = isStable;
                    distributionSummaryObject["version"] = version;
                    distributionSummaryObject["failureCount"] = failureCount;

                    if (!string.Equals(state, "pass", StringComparison.Ordinal))
                    {
                        AddBlockingReason(
                            category: "governance",
                            invariantId: DistributionReadinessDecisionInvariantId,
                            sourceArtifact: "artifacts/test-results/distribution-readiness-governance-report.json",
                            expected: "distribution summary state = pass",
                            actual: $"distribution summary state = {state}");
                    }
                }

                var failureNodes = distributionNode["failures"] as JsonArray;
                if (failureNodes is not null)
                {
                    foreach (var node in failureNodes.OfType<JsonObject>())
                    {
                        var category = node["category"]?.GetValue<string>() ?? "package-metadata";
                        var invariantId = node["invariantId"]?.GetValue<string>() ?? DistributionReadinessDecisionInvariantId;
                        var sourceArtifact = node["sourceArtifact"]?.GetValue<string>() ?? "artifacts/packages";
                        var expected = node["expected"]?.GetValue<string>() ?? "distribution policy satisfied";
                        var actual = node["actual"]?.GetValue<string>() ?? "violation";

                        distributionFailureArray.Add(new JsonObject
                        {
                            ["category"] = category,
                            ["invariantId"] = invariantId,
                            ["sourceArtifact"] = sourceArtifact,
                            ["expected"] = expected,
                            ["actual"] = actual
                        });

                        AddBlockingReason(
                            category: category,
                            invariantId: invariantId,
                            sourceArtifact: sourceArtifact,
                            expected: expected,
                            actual: actual);
                    }
                }
            }

            var adoptionSummaryObject = new JsonObject
            {
                ["state"] = "unknown",
                ["blockingFindingCount"] = -1,
                ["advisoryFindingCount"] = -1,
                ["sourceArtifact"] = "artifacts/test-results/adoption-readiness-governance-report.json"
            };
            var adoptionBlockingArray = new JsonArray();
            var adoptionAdvisoryArray = new JsonArray();
            if (File.Exists(AdoptionReadinessGovernanceReportFile))
            {
                var adoptionNode = JsonNode.Parse(File.ReadAllText(AdoptionReadinessGovernanceReportFile))?.AsObject()
                    ?? new JsonObject();
                var summaryNode = adoptionNode["summary"] as JsonObject;
                if (summaryNode is null)
                {
                    AddBlockingReason(
                        category: "evidence",
                        invariantId: AdoptionReadinessSchemaInvariantId,
                        sourceArtifact: "artifacts/test-results/adoption-readiness-governance-report.json",
                        expected: "summary section present",
                        actual: "summary section missing");
                }
                else
                {
                    adoptionSummaryObject["state"] = summaryNode["state"]?.GetValue<string>() ?? "unknown";
                    adoptionSummaryObject["blockingFindingCount"] = summaryNode["blockingFindingCount"]?.GetValue<int>() ?? -1;
                    adoptionSummaryObject["advisoryFindingCount"] = summaryNode["advisoryFindingCount"]?.GetValue<int>() ?? -1;
                }

                var blockingNodes = adoptionNode["blockingFindings"] as JsonArray;
                if (blockingNodes is not null)
                {
                    foreach (var node in blockingNodes.OfType<JsonObject>())
                    {
                        var category = node["category"]?.GetValue<string>() ?? "governance";
                        var invariantId = node["invariantId"]?.GetValue<string>() ?? AdoptionReadinessPolicyInvariantId;
                        var sourceArtifact = node["sourceArtifact"]?.GetValue<string>() ?? "artifacts/test-results/adoption-readiness-governance-report.json";
                        var expected = node["expected"]?.GetValue<string>() ?? "blocking adoption findings absent";
                        var actual = node["actual"]?.GetValue<string>() ?? "blocking finding present";

                        adoptionBlockingArray.Add(new JsonObject
                        {
                            ["policyTier"] = node["policyTier"]?.GetValue<string>() ?? "blocking",
                            ["category"] = category,
                            ["invariantId"] = invariantId,
                            ["sourceArtifact"] = sourceArtifact,
                            ["expected"] = expected,
                            ["actual"] = actual
                        });

                        AddBlockingReason(
                            category: "governance",
                            invariantId: invariantId,
                            sourceArtifact: sourceArtifact,
                            expected: expected,
                            actual: actual);
                    }
                }

                var advisoryNodes = adoptionNode["advisoryFindings"] as JsonArray;
                if (advisoryNodes is not null)
                {
                    foreach (var node in advisoryNodes.OfType<JsonObject>())
                    {
                        adoptionAdvisoryArray.Add(new JsonObject
                        {
                            ["policyTier"] = node["policyTier"]?.GetValue<string>() ?? "advisory",
                            ["category"] = node["category"]?.GetValue<string>() ?? "governance",
                            ["invariantId"] = node["invariantId"]?.GetValue<string>() ?? AdoptionReadinessPolicyInvariantId,
                            ["sourceArtifact"] = node["sourceArtifact"]?.GetValue<string>() ?? "artifacts/test-results/adoption-readiness-governance-report.json",
                            ["expected"] = node["expected"]?.GetValue<string>() ?? "advisory finding absent",
                            ["actual"] = node["actual"]?.GetValue<string>() ?? "advisory finding present"
                        });
                    }
                }
            }

            var mainPackagePattern = "Agibuild.Fulora.Avalonia.*.nupkg";
            var hasCanonicalMainPackage = PackageOutputDirectory.GlobFiles(mainPackagePattern)
                .Any(path => !path.Name.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
            if (!hasCanonicalMainPackage)
            {
                AddBlockingReason(
                    category: "package-metadata",
                    invariantId: ReleaseOrchestrationDecisionInvariantId,
                    sourceArtifact: "artifacts/packages",
                    expected: $"at least one package matching {mainPackagePattern}",
                    actual: "none");
            }

            string? packedVersion = null;
            try
            {
                packedVersion = ResolvePackedAgibuildVersion(PrimaryHostPackageId);
            }
            catch (Exception ex)
            {
                AddBlockingReason(
                    category: "package-metadata",
                    invariantId: StablePublishReadinessInvariantId,
                    sourceArtifact: "artifacts/packages",
                    expected: "packed version resolved for Agibuild.Fulora.Avalonia",
                    actual: ex.Message);
            }

            var isStableRelease = packedVersion is not null && !packedVersion.Contains('-', StringComparison.Ordinal);
            var decisionState = blockingReasons.Count == 0 ? "ready" : "blocked";
            var evaluatedAtUtc = DateTime.UtcNow.ToString("o");

            var decisionObject = new JsonObject
            {
                ["state"] = decisionState,
                ["isStableRelease"] = isStableRelease,
                ["version"] = packedVersion ?? "unknown",
                ["laneContext"] = LaneContextCi,
                ["producerTarget"] = "ReleaseOrchestrationGovernance",
                ["evaluatedAtUtc"] = evaluatedAtUtc,
                ["blockingReasonCount"] = blockingReasons.Count
            };

            var blockingReasonArray = new JsonArray(
                blockingReasons.Select(reason => new JsonObject
                {
                    ["category"] = reason.Category,
                    ["invariantId"] = reason.InvariantId,
                    ["sourceArtifact"] = reason.SourceArtifact,
                    ["expected"] = reason.Expected,
                    ["actual"] = reason.Actual
                }).ToArray());

            if (File.Exists(CloseoutSnapshotFile))
            {
                var snapshotNode = JsonNode.Parse(File.ReadAllText(CloseoutSnapshotFile))?.AsObject()
                    ?? new JsonObject();
                snapshotNode["distributionReadiness"] = distributionSummaryObject;
                snapshotNode["distributionReadinessFailures"] = distributionFailureArray;
                snapshotNode["adoptionReadiness"] = adoptionSummaryObject;
                snapshotNode["adoptionBlockingFindings"] = adoptionBlockingArray;
                snapshotNode["adoptionAdvisoryFindings"] = adoptionAdvisoryArray;
                snapshotNode["releaseDecision"] = decisionObject;
                snapshotNode["releaseBlockingReasons"] = blockingReasonArray;

                File.WriteAllText(
                    CloseoutSnapshotFile,
                    snapshotNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                Serilog.Log.Information("Closeout snapshot updated with release decision payload at {Path}", CloseoutSnapshotFile);
            }

            var reportPayload = new
            {
                schemaVersion = 1,
                provenance = new
                {
                    laneContext = LaneContextCi,
                    producerTarget = "ReleaseOrchestrationGovernance",
                    timestamp = evaluatedAtUtc
                },
                decision = new
                {
                    state = decisionState,
                    isStableRelease,
                    version = packedVersion ?? "unknown",
                    blockingReasonCount = blockingReasons.Count
                },
                distributionReadiness = new
                {
                    state = distributionSummaryObject["state"]?.GetValue<string>() ?? "unknown",
                    isStableRelease = distributionSummaryObject["isStableRelease"]?.GetValue<bool>() ?? false,
                    version = distributionSummaryObject["version"]?.GetValue<string>() ?? "unknown",
                    failureCount = distributionSummaryObject["failureCount"]?.GetValue<int>() ?? -1
                },
                adoptionReadiness = new
                {
                    state = adoptionSummaryObject["state"]?.GetValue<string>() ?? "unknown",
                    blockingFindingCount = adoptionSummaryObject["blockingFindingCount"]?.GetValue<int>() ?? -1,
                    advisoryFindingCount = adoptionSummaryObject["advisoryFindingCount"]?.GetValue<int>() ?? -1
                },
                blockingReasons = blockingReasons.Select(reason => new
                {
                    category = reason.Category,
                    invariantId = reason.InvariantId,
                    sourceArtifact = reason.SourceArtifact,
                    expected = reason.Expected,
                    actual = reason.Actual
                }).ToArray()
            };

            WriteJsonReport(ReleaseOrchestrationDecisionReportFile, reportPayload);
            Serilog.Log.Information("Release orchestration decision report written to {Path}", ReleaseOrchestrationDecisionReportFile);

            if (string.Equals(decisionState, "blocked", StringComparison.Ordinal))
            {
                var lines = blockingReasons.Select(reason =>
                    $"- [{reason.Category}] [{reason.InvariantId}] {reason.SourceArtifact}: expected {reason.Expected}, actual {reason.Actual}");
                Assert.Fail("Release orchestration governance blocked publication:\n" + string.Join('\n', lines));
            }
        });
}
