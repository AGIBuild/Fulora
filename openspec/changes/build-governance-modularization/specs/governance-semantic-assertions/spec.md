## MODIFIED Requirements

### Requirement: Governance diagnostics use invariant IDs
Governance failures SHALL emit machine-readable diagnostics with stable invariant IDs, source artifact paths, and expected-vs-actual values. The diagnostic schema MUST use the typed `GovernanceFailure` record. All existing invariant IDs MUST be preserved during migration to the new infrastructure. Report payloads follow the standard envelope convention (generatedAtUtc, failureCount, failures) with domain-specific extensions.

#### Scenario: Existing invariant IDs preserved
- **WHEN** governance targets are migrated to use GovernanceFailure in GovernanceCheckResult
- **THEN** every pre-existing invariant ID in governance report JSON MUST remain identical

#### Scenario: Report JSON field compatibility
- **WHEN** a governance report is generated after migration
- **THEN** it MUST contain the same semantic camelCase fields as before (generatedAtUtc, checks/failures, counts) — no field name changes or removals

#### Scenario: Downstream report consumer compatibility
- **WHEN** ReleaseOrchestrationGovernance reads upstream governance reports
- **THEN** all fields it accesses (failureCount, summary.state, failures[].category, failures[].invariantId, blockingFindings[].policyTier, etc.) MUST remain at the same JSON paths with the same camelCase names

#### Scenario: GovernanceFailure fields serialize as camelCase
- **WHEN** GovernanceFailure records are serialized in a governance report
- **THEN** the JSON fields MUST be `category`, `invariantId`, `sourceArtifact`, `expected`, `actual` (camelCase) to match the existing conventions used by downstream consumers
