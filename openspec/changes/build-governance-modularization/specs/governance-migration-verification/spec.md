## ADDED Requirements

### Requirement: Report JSON field-level compatibility after GovernanceFailure migration
After migrating governance targets to use GovernanceFailure, all report JSON files SHALL retain field-level compatibility with pre-migration output.

#### Scenario: Before/after report comparison for Tier 1 targets
- **GIVEN** pre-migration governance report JSON for each Tier 1 target (Dependency, TypeScript, Sample, Solution, BridgeDistribution)
- **WHEN** the same target runs after migration to GovernanceFailure
- **THEN** the report JSON MUST contain identical top-level field names (camelCase), identical `failureCount` values, and identical invariant IDs in the `failures` array

#### Scenario: GovernanceFailure fields serialize at correct JSON paths
- **GIVEN** a governance report containing GovernanceFailure entries in its `failures` array
- **WHEN** the report is parsed as JSON
- **THEN** each failure object MUST have fields `category`, `invariantId`, `sourceArtifact`, `expected`, `actual` at the first level of the failure object

### Requirement: Downstream consumer contract validation
ReleaseOrchestrationGovernance SHALL successfully parse all upstream governance reports after migration without errors or data loss.

#### Scenario: Distribution readiness report consumption
- **GIVEN** a migrated `distribution-readiness-governance-report.json`
- **WHEN** ReleaseOrchestrationGovernance reads `summary.state`, `summary.isStableRelease`, `summary.version`, `summary.failureCount`, and `failures[].{category, invariantId, sourceArtifact, expected, actual}`
- **THEN** all fields MUST resolve to non-null values matching the producing target's output

#### Scenario: Adoption readiness report consumption
- **GIVEN** a migrated `adoption-readiness-governance-report.json`
- **WHEN** ReleaseOrchestrationGovernance reads `summary.state`, `summary.blockingFindingCount`, `summary.advisoryFindingCount`, `blockingFindings[].{policyTier, category, invariantId, sourceArtifact, expected, actual}`, and `advisoryFindings[]`
- **THEN** all fields MUST resolve to non-null values matching the producing target's output

#### Scenario: Transition gate report consumption
- **GIVEN** a migrated `transition-gate-governance-report.json`
- **WHEN** ReleaseOrchestrationGovernance reads `failureCount`
- **THEN** the value MUST be an integer matching the count of GovernanceFailure entries

#### Scenario: Closeout snapshot report consumption
- **GIVEN** a `closeout-snapshot.json` (unchanged by this migration)
- **WHEN** ReleaseOrchestrationGovernance reads `coverage.linePercent`, `coverage.branchPercent`, `governance` section
- **THEN** all fields MUST resolve correctly as before migration

### Requirement: CI dependency graph preservation
The Ci target's effective execution graph SHALL remain semantically identical after dependency simplification.

#### Scenario: Transitive dependency coverage
- **GIVEN** the current Ci target depends on N governance targets directly
- **WHEN** redundant direct dependencies are removed (targets already covered transitively via ReleaseOrchestrationGovernance)
- **THEN** `nuke Ci --plan` MUST list the same set of targets in the execution plan

#### Scenario: No governance target dropped
- **WHEN** comparing the Ci execution plan before and after simplification
- **THEN** no governance target SHALL be absent from the post-simplification plan

### Requirement: Unit test coherence
Existing governance unit tests SHALL pass after migration without false positives or false negatives.

#### Scenario: File path references updated
- **GIVEN** `AutomationLaneGovernanceTests.cs` validates governance sources via paths such as `"build/Build*.cs"`
- **WHEN** governance targets are decomposed into multiple partial files
- **THEN** test assertions MUST reference the current partial-file layout instead of the removed monolithic file path

#### Scenario: JSON schema assertions remain valid
- **GIVEN** tests that assert governance report JSON structure (e.g., `Ci_evidence_snapshot_build_target_emits_v2_schema_with_provenance`)
- **WHEN** the report schema is unchanged by this migration
- **THEN** those tests MUST continue to pass without modification
