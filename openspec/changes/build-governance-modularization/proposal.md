## Why

Build.Governance.cs has grown to 1,907 lines with 11 governance targets sharing an identical report-generation pattern (create failures list → run checks → build anonymous payload → WriteJsonReport → Assert.Fail). Each target defines its own failure record type with the same shape. This duplication increases maintenance cost, makes adding new governance targets error-prone, and obscures the actual governance logic behind boilerplate. Refactoring the governance layer directly advances post-Phase 12 maintenance by reducing build system complexity — aligned with the project's "design for extensibility" principle.

## Non-goals

- Changing governance semantics or thresholds
- Modifying CI workflow files
- Altering the Ci/CiPublish target dependency graph semantics (only reducing redundant direct edges)

## What Changes

- Extract a reusable `GovernanceRunner` helper that encapsulates the report-generation boilerplate (directory creation, failure collection, JSON report writing, assertion)
- Introduce a unified `GovernanceFailure` record replacing 5+ scattered record types with identical shape
- Introduce `GovernanceReportPayload<T>` to replace anonymous objects for report serialization
- Split `Build.Governance.cs` into domain-specific partial files:
  - `Build.Governance.Dependency.cs` (DependencyVulnerabilityGovernance)
  - `Build.Governance.TypeScript.cs` (TypeScriptDeclarationGovernance)
  - `Build.Governance.Sample.cs` (SampleTemplatePackageReferenceGovernance)
  - `Build.Governance.Release.cs` (ReleaseCloseoutSnapshot, ContinuousTransitionGateGovernance, ReleaseOrchestrationGovernance)
  - `Build.Governance.Distribution.cs` (BridgeDistributionGovernance, DistributionReadinessGovernance, AdoptionReadinessGovernance)
  - `Build.Governance.RuntimePath.cs` (RuntimeCriticalPathExecutionGovernance)
  - `Build.Governance.OpenSpec.cs` (OpenSpecStrictGovernance)
- Simplify `Ci` target's direct dependency list by removing targets that are already transitively required

## Capabilities

### New Capabilities
- `build-governance-infrastructure`: Shared governance execution and reporting infrastructure (GovernanceRunner, GovernanceFailure, GovernanceReportPayload)

### Modified Capabilities
- `governance-semantic-assertions`: Governance report schema evolves from anonymous objects to typed payloads while preserving invariant IDs and expected-vs-actual semantics

## Impact

- **Build system files**: `build/Build.Governance.cs` (split into 7+ files), `build/Build.cs` (Ci target dependency list), `build/Build.WarningGovernance.cs` (adopt GovernanceRunner), `build/Build.Testing.cs` (AutomationLaneReport adopts pattern)
- **CI artifacts**: Report JSON files retain same field semantics — downstream consumers (CI workflow, release pipeline) are unaffected
- **No application code changes**
