## ADDED Requirements

### Requirement: Governance semantic assertions SHALL verify CI-release version provenance parity
Governance checks SHALL assert that release-published version values are identical to the CI-produced manifest version and that both are derived from the same repository baseline source.

#### Scenario: Version provenance parity passes
- **WHEN** CI evidence manifest and release publish inputs are evaluated
- **THEN** semantic assertions confirm version equality across CI manifest and release publish payload
- **AND** diagnostics include baseline source identity used for version derivation

#### Scenario: Version provenance parity fails deterministically
- **WHEN** release publish version differs from CI manifest version or baseline source identity is missing
- **THEN** governance fails with invariant-keyed diagnostics containing expected-vs-actual version and source metadata

### Requirement: Governance semantic assertions SHALL enforce no-rebuild promotion policy
Governance checks SHALL assert that release lane does not execute package rebuild steps for promotable artifacts and only consumes CI-produced immutable artifacts.

#### Scenario: No-rebuild promotion policy passes
- **WHEN** release orchestration evidence indicates artifact download and publish actions only
- **THEN** semantic assertions pass for no-rebuild promotion invariant

#### Scenario: Rebuild attempt fails governance
- **WHEN** release lane evidence contains package build/pack execution after CI artifact generation
- **THEN** governance fails deterministically with lane context and violated invariant identifier

### Requirement: Governance diagnostics SHALL expose workflow authority transitions
When tag-driven workflow authority is disabled for version computation, governance diagnostics SHALL include explicit authority metadata to indicate CI manifest authority and tag metadata role.

#### Scenario: Authority metadata is present
- **WHEN** governance evaluates release authority invariants
- **THEN** diagnostic payload includes authority mode, version source, and tag-role classification
