## ADDED Requirements

### Requirement: GovernanceRunner encapsulates report lifecycle
The build system SHALL provide `RunGovernanceCheck` (sync) and `RunGovernanceCheckAsync` (async) static helper methods that encapsulate the governance target execution lifecycle: directory creation, failure collection via delegate, report payload writing, and conditional assertion on failures.

#### Scenario: Governance target uses RunGovernanceCheck
- **WHEN** a governance target executes via RunGovernanceCheck
- **THEN** the runner MUST create the TestResultsDirectory, invoke the check delegate, write the JSON report to the specified path, and Assert.Fail if any GovernanceFailure instances are returned

#### Scenario: RunGovernanceCheck formats assertion messages from GovernanceFailure
- **WHEN** RunGovernanceCheck detects failures
- **THEN** the assertion message MUST format each GovernanceFailure as `[{InvariantId}] {SourceArtifact}: expected {Expected}, actual {Actual}`

### Requirement: Unified GovernanceFailure record
All governance targets using GovernanceCheckResult SHALL use a single `GovernanceFailure` record type with fields: `Category`, `InvariantId`, `SourceArtifact`, `Expected`, `Actual`.

#### Scenario: Governance target reports a failure
- **WHEN** a governance check detects a violation
- **THEN** it MUST create a GovernanceFailure with all five fields populated

#### Scenario: Legacy string failures replaced
- **WHEN** a governance target previously used `List<string>` for failures in GovernanceCheckResult
- **THEN** it MUST be migrated to construct GovernanceFailure instances with appropriate Category, InvariantId, SourceArtifact, Expected, and Actual values

#### Scenario: TransitionGateDiagnosticEntry mapped to GovernanceFailure
- **WHEN** ContinuousTransitionGateGovernance reports a diagnostic
- **THEN** it MUST map TransitionGateDiagnosticEntry to GovernanceFailure: Group maps to Category, ArtifactPath maps to SourceArtifact, Lane is encoded in the Expected/Actual fields or Category prefix

### Requirement: GovernanceCheckResult uses GovernanceFailure
GovernanceCheckResult SHALL use `IReadOnlyList<GovernanceFailure>` instead of `IReadOnlyList<string>` for its Failures field.

#### Scenario: GovernanceCheckResult type signature
- **WHEN** GovernanceCheckResult is instantiated
- **THEN** the Failures parameter MUST accept `IReadOnlyList<GovernanceFailure>`

#### Scenario: ReportPayload remains flexible
- **WHEN** a governance target constructs a GovernanceCheckResult
- **THEN** the ReportPayload parameter MUST remain `object` to allow domain-specific report schemas

### Requirement: Standard report envelope convention
All governance reports (except plain-text outputs like OpenSpecStrictGovernance) SHALL include standard envelope fields in their JSON output.

#### Scenario: Standard envelope fields present
- **WHEN** a governance report is written
- **THEN** the report JSON MUST contain at minimum `generatedAtUtc` (string), `failureCount` (int), and `failures` (array)

#### Scenario: Domain-specific fields are additive
- **WHEN** a governance target has domain-specific diagnostic data (e.g., scans, checks, semanticDiagnostics)
- **THEN** it MAY include additional fields alongside the standard envelope without restriction

### Requirement: camelCase JSON serialization for governance reports
Governance reports SHALL be serialized using `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` and `WriteIndented = true`.

#### Scenario: GovernanceFailure serializes as camelCase
- **WHEN** a GovernanceFailure record is included in a report payload
- **THEN** its fields MUST serialize as `category`, `invariantId`, `sourceArtifact`, `expected`, `actual` (camelCase)

#### Scenario: Existing anonymous object compatibility
- **WHEN** anonymous objects with camelCase member names are serialized
- **THEN** the camelCase naming policy MUST NOT alter their existing field names

### Requirement: Governance file decomposition (completed)
Build governance logic SHALL be organized into domain-specific `Build.Governance.*.cs` partial class files. Each file MUST contain only the governance targets for its domain.

#### Scenario: File organization
- **WHEN** a developer needs to modify the DependencyVulnerabilityGovernance target
- **THEN** they MUST find it in `Build.Governance.Dependency.cs`
