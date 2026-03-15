## ADDED Requirements

### Requirement: GovernanceRunner encapsulates report lifecycle
The build system SHALL provide a `GovernanceRunner` helper that encapsulates the governance target execution lifecycle: directory creation, failure collection, report payload construction, JSON report writing, and assertion on failures.

#### Scenario: Governance target uses GovernanceRunner
- **WHEN** a governance target executes via GovernanceRunner
- **THEN** the runner MUST create the TestResultsDirectory, invoke the check delegate, write the JSON report to the specified path, and Assert.Fail if any failures exist

#### Scenario: GovernanceRunner produces consistent report structure
- **WHEN** GovernanceRunner writes a report
- **THEN** the report JSON MUST contain `generatedAtUtc`, `targetName`, `failures`, and `failureCount` fields

### Requirement: Unified GovernanceFailure record
All governance targets SHALL use a single `GovernanceFailure` record type with fields: `Category`, `InvariantId`, `SourceArtifact`, `Expected`, `Actual`.

#### Scenario: Governance target reports a failure
- **WHEN** a governance check detects a violation
- **THEN** it MUST create a GovernanceFailure with all five fields populated

#### Scenario: Legacy failure records replaced
- **WHEN** a governance target previously used a target-specific failure record
- **THEN** it MUST be migrated to GovernanceFailure without losing field semantics

### Requirement: Typed GovernanceReportPayload
Governance reports SHALL use a typed `GovernanceReportPayload<T>` record instead of anonymous objects for JSON serialization.

#### Scenario: Report deserialization
- **WHEN** a governance report is read by a downstream consumer (e.g., ReleaseOrchestrationGovernance reading RuntimeCriticalPath report)
- **THEN** the report MUST be deserializable via the typed payload without dynamic JSON parsing

### Requirement: Governance file decomposition
Build.Governance.cs SHALL be split into domain-specific partial class files. Each file MUST contain only the governance targets for its domain.

#### Scenario: File organization
- **WHEN** a developer needs to modify the DependencyVulnerabilityGovernance target
- **THEN** they MUST find it in Build.Governance.Dependency.cs, not in a monolithic file
