## Context

Build.Governance.cs is 1,907 lines containing 11 governance targets. Each target follows an identical pattern: create failures list, run checks, build anonymous report payload, write JSON, assert. Five separate record types share the same (Category, InvariantId, SourceArtifact, Expected, Actual) shape. This duplication inflates maintenance cost and makes adding new governance targets error-prone.

## Goals / Non-Goals

**Goals:**
- Extract reusable governance execution infrastructure
- Unify failure record types into a single GovernanceFailure
- Replace anonymous report payloads with typed GovernanceReportPayload
- Split the monolithic file into domain-specific partials
- Preserve all existing governance semantics and invariant IDs

**Non-Goals:**
- Changing governance check logic or thresholds
- Modifying CI workflow files
- Changing the Ci/CiPublish target graph semantics

## Decisions

1. **GovernanceRunner as a static helper method**: Rather than a base class (which doesn't fit Nuke's partial class model), use a static method `RunGovernanceCheck` that accepts a delegate returning check results and a report path. This avoids inheritance complexity.

2. **GovernanceFailure replaces all domain-specific records**: `TransitionGateDiagnosticEntry`, `ReleaseOrchestrationBlockingReason`, `DistributionReadinessFailure`, `AdoptionReadinessFinding`, and similar records are replaced by a single `GovernanceFailure(string Category, string InvariantId, string SourceArtifact, string Expected, string Actual)`.

3. **GovernanceReportPayload<T>**: A generic record `GovernanceReportPayload<T>(string GeneratedAtUtc, string TargetName, string LaneContext, IReadOnlyList<T> Findings, int FailureCount)` replaces anonymous objects.

4. **File split strategy**: One file per governance domain, named `Build.Governance.{Domain}.cs`. Shared infrastructure in `Build.Governance.Infrastructure.cs`.

5. **Ci target dependency simplification**: Remove targets from Ci's DependsOn that are already transitively required through ReleaseOrchestrationGovernance, since Nuke resolves the full graph.

## Risks / Trade-offs

- **Report JSON schema change**: Fields like `generatedAtUtc` become `GeneratedAtUtc` (PascalCase) in typed records. Mitigate by configuring JsonSerializerOptions with camelCase policy for report writing.
- **Downstream report consumers**: ReleaseOrchestrationGovernance reads other governance reports. The typed payload must remain deserializable. Mitigate by using the same GovernanceReportPayload type for both writing and reading.
- **Merge conflicts with active change**: `ci-release-unified-readiness-version-1-5` is in progress. Coordinate to avoid conflicting changes to Build.Governance.cs.
