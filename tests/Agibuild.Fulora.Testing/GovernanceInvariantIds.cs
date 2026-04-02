namespace Agibuild.Fulora.Testing;

/// <summary>
/// Stable identifiers for governance invariants. Referenced in governance test diagnostics
/// and CI evidence artifacts for deterministic failure triage.
/// </summary>
public static class GovernanceInvariantIds
{
    public const string AutomationLaneManifestSchema = "GOV-001";
    public const string RuntimeCriticalPathScenarioPresence = "GOV-002";
    public const string RuntimeCriticalPathEvidenceLinkage = "GOV-003";
    public const string SystemIntegrationCtMatrixSchema = "GOV-004";
    public const string WarningGovernanceBaseline = "GOV-005";
    public const string ShellProductionMatrixSchema = "GOV-006";
    public const string ShellManifestMatrixSync = "GOV-007";
    public const string BenchmarkBaselineSchema = "GOV-008";
    public const string BuildPipelineTargetGraph = "GOV-009";
    public const string PackageMetadata = "GOV-010";
    public const string XunitVersionAlignment = "GOV-011";
    public const string TemplateMetadataSchema = "GOV-012";
    public const string BridgeDxAssets = "GOV-013";
    public const string WebView2ReferenceModel = "GOV-014";
    public const string CoverageThreshold = "GOV-015";
    public const string ReadmeQualitySignals = "GOV-016";
    public const string WindowsBaseConflictGovernance = "GOV-017";
    public const string CiTargetDocsFirstGovernance = "GOV-018";
    public const string PhaseCloseoutConsistency = "GOV-019";
    public const string EvidenceContractV2Schema = "GOV-020";
    public const string BridgeDistributionParity = "GOV-021";
    public const string PhaseTransitionConsistency = "GOV-022";
    public const string HostNeutralDependencyBoundary = "GOV-023";
    public const string TransitionGateParityConsistency = "GOV-024";
    public const string TransitionLaneProvenanceConsistency = "GOV-025";
    public const string TransitionGateDiagnosticSchema = "GOV-026";
    public const string ReleaseOrchestrationDecisionGate = "GOV-027";
    public const string ReleaseOrchestrationReasonSchema = "GOV-028";
    public const string StablePublishReadiness = "GOV-029";
    public const string DistributionReadinessGate = "GOV-030";
    public const string DistributionReadinessSchema = "GOV-031";
    public const string AdoptionReadinessSchema = "GOV-032";
    public const string AdoptionReadinessPolicy = "GOV-033";
    public const string ReleaseEvidenceReadinessSections = "GOV-034";
    public const string BridgeSingleEntryAppLayerPolicy = "GOV-035";
    public const string SampleTemplatePackageReferencePolicy = "GOV-036";
    public const string TestBeforePackOrdering = "GOV-037";
    public const string VersionManagementTarget = "GOV-038";
    public const string DocsWorkflowCallableOnly = "GOV-039";
    public const string WorkflowNode24Compatibility = "GOV-040";
}
