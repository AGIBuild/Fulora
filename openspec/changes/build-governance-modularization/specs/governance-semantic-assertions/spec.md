## MODIFIED Requirements

### Requirement: Governance diagnostics use invariant IDs
Governance failures SHALL emit machine-readable diagnostics with stable invariant IDs, source artifact paths, and expected-vs-actual values. The diagnostic schema MUST use the typed `GovernanceFailure` record and `GovernanceReportPayload<T>` wrapper. All existing invariant IDs MUST be preserved during migration to the new infrastructure.

#### Scenario: Existing invariant IDs preserved
- **WHEN** governance targets are migrated to GovernanceRunner
- **THEN** every pre-existing invariant ID in governance report JSON MUST remain identical

#### Scenario: Report JSON field compatibility
- **WHEN** a governance report is generated after migration
- **THEN** it MUST contain the same semantic fields as before (generatedAtUtc, checks/failures, counts) with the addition of a `targetName` field
