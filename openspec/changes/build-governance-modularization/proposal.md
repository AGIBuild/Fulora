## Why

The governance layer was originally a monolithic 1,907-line Build.Governance.cs with 11 governance targets sharing an identical report-generation pattern. The file split into 9 domain-specific partials and the initial `RunGovernanceCheck` infrastructure have been completed. However, the failure model remains fragmented: `GovernanceCheckResult.Failures` uses `IReadOnlyList<string>`, `TransitionGateDiagnosticEntry` exists alongside `GovernanceFailure`, and report payloads use anonymous objects that would break camelCase compatibility if GovernanceFailure records were serialized directly. This remaining inconsistency increases maintenance cost when adding new governance targets and prevents structured machine analysis of governance reports.

## Non-goals

- Changing governance semantics or thresholds
- Modifying CI workflow files
- Altering the Ci/CiPublish target dependency graph semantics (only reducing redundant direct edges)
- Migrating WarningGovernance, AutomationLaneReport, or OpenSpecStrictGovernance (these have specialized schemas outside the governance check pattern)

## What Changes

- Evolve `GovernanceCheckResult.Failures` from `IReadOnlyList<string>` to `IReadOnlyList<GovernanceFailure>` for type-safe failure reporting
- Migrate the 5 targets currently using RunGovernanceCheck (Dependency, TypeScript, Sample, Solution, BridgeDistribution) to construct GovernanceFailure instead of string failures
- Migrate RuntimeCriticalPathExecutionGovernance and ContinuousTransitionGateGovernance to use RunGovernanceCheck with GovernanceFailure
- Retire `TransitionGateDiagnosticEntry` by mapping to GovernanceFailure (keeping it as an internal intermediate is acceptable)
- Add `GovernanceCamelCaseJsonOptions` for report serialization to ensure GovernanceFailure records serialize with camelCase field names matching downstream consumer expectations
- Establish a standard report envelope convention (generatedAtUtc, failureCount, failures) without introducing a rigid `GovernanceReportPayload<T>` generic type
- Simplify `Ci` target's direct dependency list by removing targets that are already transitively required

## Capabilities

### New Capabilities
- `build-governance-infrastructure`: Shared governance execution and reporting infrastructure — GovernanceRunner (RunGovernanceCheck), GovernanceFailure, GovernanceCamelCaseJsonOptions, standard report envelope convention

### Modified Capabilities
- `governance-semantic-assertions`: Governance report failures migrate from string to GovernanceFailure while preserving invariant IDs, expected-vs-actual semantics, and camelCase JSON field names

## Impact

- **Build system files**: `build/Build.Governance.Infrastructure.cs` (GovernanceCheckResult type change, camelCase options), `build/Build.Governance.{Dependency,TypeScript,Sample,Solution}.cs` (GovernanceFailure migration), `build/Build.Governance.RuntimePath.cs` (adopt RunGovernanceCheck), `build/Build.Governance.Release.cs` (TransitionGate GovernanceFailure migration), `build/Build.cs` (Ci target dependency simplification), `build/Build.ProcessHelpers.cs` (GovernanceCamelCaseJsonOptions)
- **CI artifacts**: Report JSON files retain same camelCase field semantics — downstream consumers (CI workflow, release pipeline, ReleaseOrchestrationGovernance) are unaffected
- **No application code changes**
- **Test files**: `AutomationLaneGovernanceTests.cs` — update file path references from `"build/Build.Governance.cs"` to `"build/Build.Governance.Release.cs"` or `"build/Build*.cs"` in error messages
