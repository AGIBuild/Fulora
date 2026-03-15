## Context

The governance layer in build/ was originally a monolithic 1,907-line Build.Governance.cs. The file split into domain-specific partials has already been completed, yielding 9 files:

| File | Targets |
|------|---------|
| `Build.Governance.Infrastructure.cs` | GovernanceFailure, GovernanceCheckResult, RunGovernanceCheck/Async |
| `Build.Governance.Dependency.cs` | DependencyVulnerabilityGovernance |
| `Build.Governance.TypeScript.cs` | TypeScriptDeclarationGovernance |
| `Build.Governance.Sample.cs` | SampleTemplatePackageReferenceGovernance |
| `Build.Governance.RuntimePath.cs` | RuntimeCriticalPathExecutionGovernance |
| `Build.Governance.OpenSpec.cs` | OpenSpecStrictGovernance |
| `Build.Governance.Distribution.cs` | BridgeDistributionGovernance, DistributionReadinessGovernance, AdoptionReadinessGovernance |
| `Build.Governance.Release.cs` | ReleaseCloseoutSnapshot, ContinuousTransitionGateGovernance, ReleaseOrchestrationGovernance |
| `Build.Governance.Solution.cs` | SolutionConsistencyGovernance |

Remaining work: unify the failure model, standardize report conventions, and migrate targets that still use ad-hoc patterns to the shared infrastructure.

## Goals / Non-Goals

**Goals:**
- Unify failure representation to GovernanceFailure across all governance targets
- Migrate GovernanceCheckResult.Failures from `IReadOnlyList<string>` to `IReadOnlyList<GovernanceFailure>`
- Establish a standard report envelope convention (common fields) without forcing a rigid generic type
- Add camelCase JSON serialization for governance reports to support GovernanceFailure direct serialization
- Migrate remaining targets (RuntimeCriticalPath, TransitionGate, DistributionReadiness, AdoptionReadiness) to use RunGovernanceCheck where applicable
- Simplify Ci target direct dependency list by removing transitively required targets
- Preserve all existing governance semantics, invariant IDs, and report JSON field compatibility

**Non-Goals:**
- Changing governance check logic or thresholds
- Modifying CI workflow files
- Changing the Ci/CiPublish target graph semantics
- Migrating WarningGovernance or AutomationLaneReport (these have specialized schemas that do not fit the governance check pattern)
- Migrating OpenSpecStrictGovernance (outputs plain text log, not JSON governance report)

## Decisions

### Decision 1: RunGovernanceCheck as the standard lifecycle helper

Use the existing static methods `RunGovernanceCheck` (sync) and `RunGovernanceCheckAsync` (async) that accept a delegate returning `GovernanceCheckResult`. This encapsulates: directory creation, delegate invocation, report writing, and assertion on failures.

**Status**: Already implemented in `Build.Governance.Infrastructure.cs`. 5 targets already use it (Dependency, TypeScript, Sample, Solution, BridgeDistribution).

### Decision 2: GovernanceFailure as the universal failure record

`GovernanceFailure(string Category, string InvariantId, string SourceArtifact, string Expected, string Actual)` is the single failure representation for all governance targets that use GovernanceCheckResult.

**Mapping rules for existing domain-specific types:**

| Source Type | Mapping to GovernanceFailure |
|-------------|------------------------------|
| `string` failures (current GovernanceCheckResult) | Extract InvariantId from `[GOV-XXX]` prefix if present, otherwise use target's default invariant ID. Category from target domain. SourceArtifact from check context. Expected/Actual from message content. |
| Transition-gate parity/provenance checks | Category from rule group, InvariantId preserved, SourceArtifact = `build/Build*.cs` or closeout artifact path, Expected/Actual encoded with lane context |
| Inline `GovernanceFailure` in DistributionReadiness/AdoptionReadiness | Already uses GovernanceFailure directly — no mapping needed |
| `GovernanceFailure` in ReleaseOrchestrationGovernance | Already uses GovernanceFailure directly — no mapping needed |

**Exclusions** (keep separate, do not migrate to GovernanceFailure):
- `WarningObservation` / `WarningClassification` — WarningGovernance has a distinct classification model (known-baseline/actionable/new-regression) that serves a different purpose than pass/fail governance.
- `AutomationLaneResult` — AutomationLaneReport is a CI evidence collector, not a governance check.
- `OpenSpecStrictGovernance` — outputs plain text log, not structured JSON.

### Decision 3: Drop GovernanceReportPayload\<T\> in favor of standard envelope convention

After analysis, `GovernanceReportPayload<T>` is too rigid for the diverse report schemas. 14 governance targets produce reports with 3 distinct structural tiers:

**Tier 1 — Simple check reports** (use RunGovernanceCheck, domain-specific payload with standard fields):
- DependencyVulnerabilityGovernance, TypeScriptDeclarationGovernance, SampleTemplatePackageReferenceGovernance, SolutionConsistencyGovernance, BridgeDistributionGovernance

**Tier 2 — Structured governance reports** (use GovernanceFailure internally, custom schema with provenance):
- DistributionReadinessGovernance, AdoptionReadinessGovernance, ContinuousTransitionGateGovernance, RuntimeCriticalPathExecutionGovernance

**Tier 3 — Composite/orchestration targets** (complex schemas, aggregate other reports):
- ReleaseCloseoutSnapshot, ReleaseOrchestrationGovernance

**Standard envelope convention** — All governance reports (Tier 1 and 2) MUST include these common top-level fields:

| Field | Type | Description |
|-------|------|-------------|
| `generatedAtUtc` | string (ISO 8601) | Timestamp of report generation |
| `failureCount` | int | Number of failures detected |
| `failures` | GovernanceFailure[] or string[] | Failure details |

Optional standard fields (recommended for Tier 2+):

| Field | Type | Description |
|-------|------|-------------|
| `targetName` | string | Name of the producing governance target |
| `schemaVersion` | int | Report schema version for forward compatibility |
| `provenance` | object | `{ laneContext, producerTarget, timestamp }` |

Domain-specific fields (scans, checks, semanticDiagnostics, summary, parityRules, etc.) are additive and unconstrained.

### Decision 4: GovernanceCheckResult evolves to use GovernanceFailure

```
GovernanceCheckResult(IReadOnlyList<GovernanceFailure> Failures, object ReportPayload)
```

Changes from current:
- `Failures` type changes from `IReadOnlyList<string>` to `IReadOnlyList<GovernanceFailure>`
- `ReportPayload` remains `object` to allow domain-specific report shapes
- `RunGovernanceCheck` formats GovernanceFailure into assertion messages:
  `[{InvariantId}] {SourceArtifact}: expected {Expected}, actual {Actual}`

### Decision 5: camelCase JSON serialization for governance reports

Add `GovernanceCamelCaseJsonOptions`:
```
new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
```

Rationale:
- Current anonymous objects already produce camelCase (member names are lowercase by convention)
- GovernanceFailure record properties are PascalCase — without camelCase policy, direct serialization would break field compatibility
- Downstream consumers (ReleaseOrchestrationGovernance) read `node["category"]`, `node["invariantId"]` etc. — these expect camelCase
- Existing `WriteIndentedJsonOptions` (no naming policy) continues to be used by non-governance helpers (solution filter, etc.)

`WriteJsonReport` will use `GovernanceCamelCaseJsonOptions` for governance reports.

### Decision 6: File split is complete — no further structural changes

The split from monolithic Build.Governance.cs into 9 domain files is already done. The original file no longer exists. No additional file moves are needed.

### Decision 7: Ci target dependency simplification

Remove targets from Ci's DependsOn that are already transitively required through ReleaseOrchestrationGovernance. Nuke resolves the full dependency graph, so explicit listing of transitively-covered targets is redundant. ContinuousTransitionGateGovernance validates parity against Ci transitive closure instead of only direct edges.

## Downstream Report Read Contracts

ReleaseOrchestrationGovernance is the primary report consumer. It reads these fields from upstream reports:

| Report File | Fields Read | Access Pattern |
|-------------|-------------|----------------|
| `closeout-snapshot.json` | `coverage.linePercent`, `coverage.lineThreshold`, `coverage.branchPercent`, `coverage.branchThreshold`, `governance` section existence | `JsonDocument.GetProperty()` |
| `transition-gate-governance-report.json` | `failureCount` | `TryGetProperty("failureCount")` |
| `distribution-readiness-governance-report.json` | `summary.state`, `summary.isStableRelease`, `summary.version`, `summary.failureCount`, `failures[].{category, invariantId, sourceArtifact, expected, actual}` | `JsonNode["summary"]`, `JsonNode["failures"]` |
| `adoption-readiness-governance-report.json` | `summary.state`, `summary.blockingFindingCount`, `summary.advisoryFindingCount`, `blockingFindings[].{policyTier, category, invariantId, sourceArtifact, expected, actual}`, `advisoryFindings[]` | `JsonNode["summary"]`, `JsonNode["blockingFindings"]` |

All these read paths use camelCase field names. The camelCase serialization policy (Decision 5) ensures GovernanceFailure records serialize compatibly.

## Risks / Trade-offs

- **GovernanceCheckResult type change**: Changing Failures from string to GovernanceFailure requires updating all 5 targets that currently use RunGovernanceCheck. Mitigate by migrating one target at a time with before/after report comparison.
- **camelCase serialization scope**: Switching WriteJsonReport to camelCase could affect non-governance callers. Mitigate by introducing a separate method `WriteGovernanceReport` or scoping the options change to governance files only.
- **TransitionGate parity visibility**: Removing the dedicated diagnostics array reduces redundant payloads but can hide direct-edge intent if only raw failures are reported. Mitigate by including lane dependency closure context and preserving invariant IDs in failure payloads.
- **Merge conflicts**: `ci-release-unified-readiness-version-1-5` is a parallel change. Coordinate by completing this change first (it only touches build infrastructure, not governance logic) or by rebasing after that change lands.
