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

    private static readonly TransitionGateParityRule[] CloseoutCriticalTransitionGateParityRules =
    [
        new("coverage", "Coverage", "Coverage"),
        new("automation-lane-report", "AutomationLaneReport", "AutomationLaneReport"),
        new("warning-governance", "WarningGovernance", "WarningGovernance"),
        new("dependency-vulnerability-governance", "DependencyVulnerabilityGovernance", "DependencyVulnerabilityGovernance"),
        new("typescript-declaration-governance", "TypeScriptDeclarationGovernance", "TypeScriptDeclarationGovernance"),
        new("release-closeout-snapshot", "ReleaseCloseoutSnapshot", "ReleaseCloseoutSnapshot"),
        new("runtime-critical-path-governance", "RuntimeCriticalPathExecutionGovernance", "RuntimeCriticalPathExecutionGovernance"),
        new("adoption-readiness-governance", "AdoptionReadinessGovernance", "AdoptionReadinessGovernance")
    ];

    internal Target ContinuousTransitionGateGovernance => _ => _
        .Description("Validates closeout transition-gate governance targets are reachable in Ci with lane-aware failures.")
        .DependsOn(ReleaseCloseoutSnapshot)
        .Executes(() =>
        {
            RunGovernanceCheck(
                "Continuous transition gate governance",
                TransitionGateGovernanceReportFile,
                () =>
                {
                    var failures = new List<GovernanceFailure>();
                    const string buildArtifactPath = "build/Build*.cs";

                    var buildSource = ReadCombinedBuildSource();
                    var dependencyGraph = BuildTargetDependencyGraph(buildSource);
                    var ciDependencyClosure = ExpandTargetDependencies(LaneContextCi, dependencyGraph);
                    var ciPublishDirectDependencies = dependencyGraph.TryGetValue(LaneContextCiPublish, out var ciPublishDeps)
                        ? ciPublishDeps
                        : new HashSet<string>(StringComparer.Ordinal);

                    foreach (var rule in CloseoutCriticalTransitionGateParityRules)
                    {
                        if (!ciDependencyClosure.Contains(rule.CiDependency))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: rule.Group,
                                InvariantId: TransitionGateParityInvariantId,
                                SourceArtifact: buildArtifactPath,
                                Expected: $"{LaneContextCi}: dependency closure contains {rule.CiDependency}",
                                Actual: $"{LaneContextCi}: missing"));
                        }
                    }

                    if (!ciPublishDirectDependencies.Contains(LaneContextCi, StringComparer.Ordinal))
                    {
                        failures.Add(new GovernanceFailure(
                            Category: "ci-inheritance",
                            InvariantId: TransitionGateParityInvariantId,
                            SourceArtifact: buildArtifactPath,
                            Expected: $"{LaneContextCiPublish}: depends on {LaneContextCi}",
                            Actual: $"{LaneContextCiPublish}: missing"));
                    }

                    const string closeoutArtifactPath = "artifacts/test-results/closeout-snapshot.json";

                    if (!File.Exists(CloseoutSnapshotFile))
                    {
                        failures.Add(new GovernanceFailure(
                            Category: "transition-continuity",
                            InvariantId: TransitionLaneProvenanceInvariantId,
                            SourceArtifact: closeoutArtifactPath,
                            Expected: $"{LaneContextCi}: closeout snapshot exists",
                            Actual: $"{LaneContextCi}: file missing"));
                    }
                    else
                    {
                        using var closeoutDoc = JsonDocument.Parse(File.ReadAllText(CloseoutSnapshotFile));
                        var root = closeoutDoc.RootElement;
                        var provenance = root.GetProperty("provenance");
                        var transition = root.GetProperty("transition");

                        if (!string.Equals(provenance.GetProperty("laneContext").GetString(), LaneContextCi, StringComparison.Ordinal))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "transition-continuity",
                                InvariantId: TransitionLaneProvenanceInvariantId,
                                SourceArtifact: closeoutArtifactPath,
                                Expected: $"{LaneContextCi}: provenance.laneContext = {LaneContextCi}",
                                Actual: $"{LaneContextCi}: provenance.laneContext = {provenance.GetProperty("laneContext").GetString() ?? "<null>"}"));
                        }

                        if (!string.Equals(provenance.GetProperty("producerTarget").GetString(), "ReleaseCloseoutSnapshot", StringComparison.Ordinal))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "transition-continuity",
                                InvariantId: TransitionLaneProvenanceInvariantId,
                                SourceArtifact: closeoutArtifactPath,
                                Expected: $"{LaneContextCi}: provenance.producerTarget = ReleaseCloseoutSnapshot",
                                Actual: $"{LaneContextCi}: provenance.producerTarget = {provenance.GetProperty("producerTarget").GetString() ?? "<null>"}"));
                        }

                        if (!string.Equals(transition.GetProperty("governanceModel").GetString(), "docs-first", StringComparison.Ordinal))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "transition-continuity",
                                InvariantId: TransitionLaneProvenanceInvariantId,
                                SourceArtifact: closeoutArtifactPath,
                                Expected: $"{LaneContextCi}: transition.governanceModel = docs-first",
                                Actual: $"{LaneContextCi}: transition.governanceModel = {transition.GetProperty("governanceModel").GetString() ?? "<null>"}"));
                        }

                        if (!string.Equals(transition.GetProperty("releaseGovernanceDocument").GetString(), "docs/release-governance.md", StringComparison.Ordinal))
                        {
                            failures.Add(new GovernanceFailure(
                                Category: "transition-continuity",
                                InvariantId: TransitionLaneProvenanceInvariantId,
                                SourceArtifact: closeoutArtifactPath,
                                Expected: $"{LaneContextCi}: transition.releaseGovernanceDocument = docs/release-governance.md",
                                Actual: $"{LaneContextCi}: transition.releaseGovernanceDocument = {transition.GetProperty("releaseGovernanceDocument").GetString() ?? "<null>"}"));
                        }
                    }

                    var reportPayload = new
                    {
                        schemaVersion = 1,
                        generatedAtUtc = DateTime.UtcNow,
                        dependencyResolution = "transitive-closure",
                        parityRules = CloseoutCriticalTransitionGateParityRules.Select(rule => new
                        {
                            group = rule.Group,
                            ciDependency = rule.CiDependency,
                            ciPublishDependency = rule.CiPublishDependency
                        }),
                        laneDependencyClosure = new
                        {
                            ci = ciDependencyClosure.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                            ciPublish = ciPublishDirectDependencies.OrderBy(x => x, StringComparer.Ordinal).ToArray()
                        },
                        failureCount = failures.Count,
                        failures
                    };

                    return new GovernanceCheckResult(failures, reportPayload);
                });
        });

    private static string ReadCombinedBuildSource()
    {
        var buildFiles = Directory.GetFiles(RootDirectory / "build", "Build*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (buildFiles.Length == 0)
            Assert.Fail("Unable to locate any Build*.cs files under build/.");

        return string.Join(Environment.NewLine, buildFiles.Select(File.ReadAllText));
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildTargetDependencyGraph(string buildSource)
    {
        var matches = Regex.Matches(
            buildSource,
            @"Target\s+(?<target>[A-Za-z_][A-Za-z0-9_]*)\s*=>[\s\S]*?\.DependsOn\((?<deps>[\s\S]*?)\);",
            RegexOptions.Multiline);

        var graph = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (Match match in matches)
        {
            var targetName = match.Groups["target"].Value;
            var depsBlock = match.Groups["deps"].Value;
            var deps = ParseDependencyNames(depsBlock);
            graph[targetName] = deps;
        }

        if (graph.Count == 0)
            Assert.Fail("Unable to build target dependency graph from Build*.cs sources.");

        return graph;
    }

    private static IReadOnlySet<string> ParseDependencyNames(string dependsOnBlock)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var segments = dependsOnBlock.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var token = segment.Trim();
            if (token.StartsWith("nameof(", StringComparison.Ordinal) && token.EndsWith(')'))
                token = token[7..^1];

            var identifierMatch = Regex.Match(token, @"([A-Za-z_][A-Za-z0-9_]*)$");
            if (identifierMatch.Success)
                dependencies.Add(identifierMatch.Groups[1].Value);
        }

        return dependencies;
    }

    private static IReadOnlySet<string> ExpandTargetDependencies(
        string targetName,
        IReadOnlyDictionary<string, IReadOnlySet<string>> dependencyGraph)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(targetName);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!dependencyGraph.TryGetValue(current, out var dependencies))
                continue;

            foreach (var dependency in dependencies)
            {
                if (!reachable.Add(dependency))
                    continue;

                queue.Enqueue(dependency);
            }
        }

        return reachable;
    }

    internal Target ReleaseCloseoutSnapshot => _ => _
        .Description("Generates machine-readable CI evidence snapshot (v2) from test/coverage artifacts.")
        .DependsOn(Coverage, AutomationLaneReport)
        .Executes(() =>
        {
            const string transitionInvariantId = "GOV-022";
            var releaseGovernanceDocument = RootDirectory / "docs" / "release-governance.md";
            if (!File.Exists(releaseGovernanceDocument))
                Assert.Fail($"[{TransitionLaneProvenanceInvariantId}] Docs-first release governance requires docs/release-governance.md.");

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
            var totalTests = unitCounters.Total + integrationCounters.Total;
            var totalFailed = unitCounters.Failed + integrationCounters.Failed;
            var totalSkipped = unitCounters.Skipped + integrationCounters.Skipped;

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
                    governanceModel = "docs-first",
                    releaseGovernanceDocument = "docs/release-governance.md"
                },
                sourcePaths = new
                {
                    unitTrx = unitTrxPath!.ToString(),
                    integrationTrx = integrationTrxPath!.ToString(),
                    cobertura = coberturaPath!.ToString(),
                    releaseGovernance = releaseGovernanceDocument.ToString()
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
                    releaseGovernanceDocumentExists = File.Exists(releaseGovernanceDocument),
                    automationLaneReportExists = File.Exists(AutomationLaneReportFile),
                    dependencyGovernanceReportExists = File.Exists(DependencyGovernanceReportFile),
                    typeScriptGovernanceReportExists = File.Exists(TypeScriptGovernanceReportFile),
                    sampleTemplatePackageReferenceGovernanceReportExists = File.Exists(SampleTemplatePackageReferenceGovernanceReportFile),
                    runtimeCriticalPathGovernanceReportExists = File.Exists(RuntimeCriticalPathGovernanceReportFile)
                },
                closeoutSummary = new
                {
                    totalTests,
                    failedTests = totalFailed,
                    skippedTests = totalSkipped,
                    lineCoveragePercent = Math.Round(lineCoveragePct, 2),
                    branchCoveragePercent = Math.Round(branchCoveragePct, 2)
                }
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
