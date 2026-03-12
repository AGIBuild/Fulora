# governance-semantic-assertions Specification

## Purpose
Define governance assertion requirements for structured invariant validation and actionable failure diagnostics, enabling machine-readable governance tests that avoid fragile textual matching and support CI agent resolution.
## Requirements
### Requirement: Governance assertions MUST evaluate structured invariants
Governance tests SHALL validate structured invariants from machine-readable artifacts (for example JSON documents, capability mappings, and target dependency relations) instead of relying on fragile textual snippet matching.

#### Scenario: Structured invariant passes
- **WHEN** governed artifacts satisfy required schema fields and cross-artifact mapping rules
- **THEN** governance assertions pass without depending on source-code string literals

#### Scenario: Structured invariant violation fails deterministically
- **WHEN** a required invariant (such as missing mapping, invalid token, or schema mismatch) is detected
- **THEN** governance fails with deterministic machine-readable diagnostics identifying the violated invariant key

### Requirement: Governance diagnostics MUST be actionable
Governance failure output SHALL include a stable invariant identifier, affected artifact path, and expected-vs-actual summary so CI agents and maintainers can resolve failures without manual trace reconstruction.

#### Scenario: Failure output includes invariant metadata
- **WHEN** a semantic governance assertion fails
- **THEN** the emitted diagnostic contains invariant id, artifact location, and expected-vs-actual details

### Requirement: Phase transition governance SHALL be asserted by invariant IDs
Governance checks for roadmap/release closeout transitions MUST use stable invariant IDs and structured artifact fields instead of direct phase-title string coupling.

#### Scenario: Phase transition invariants pass
- **WHEN** roadmap status, closeout snapshot metadata, and governance mapping satisfy transition rules
- **THEN** governance assertions pass using stable invariant IDs without relying on phase-title literals

#### Scenario: Missing transition invariant metadata fails deterministically
- **WHEN** any required transition invariant field or mapping is absent
- **THEN** governance fails with deterministic diagnostics including invariant id, artifact path, and expected-vs-actual summary

### Requirement: Transition-gate semantic assertions SHALL evaluate lane-aware parity invariants
Governance semantic assertions MUST evaluate closeout transition gate parity as lane-aware invariants across `Ci` and `CiPublish`, including explicit mapping support for lane-context-specific target names.

#### Scenario: Lane-aware parity invariant passes
- **WHEN** both lanes satisfy all required transition-gate parity invariants
- **THEN** semantic assertions pass without relying on fragile textual matching

#### Scenario: Lane-aware parity invariant fails
- **WHEN** one or more transition-gate parity invariants are violated for a lane pair
- **THEN** semantic assertions fail deterministically with invariant-keyed diagnostics

### Requirement: Transition-gate diagnostics MUST include lane context and expected-vs-actual payload
Governance diagnostics for transition-gate semantic assertion failures SHALL include lane context, stable invariant identifier, affected artifact path, and expected-vs-actual payload values.

#### Scenario: Diagnostic payload is complete
- **WHEN** a transition-gate semantic assertion fails
- **THEN** failure output includes lane context, invariant id, artifact path, and expected-vs-actual values

### Requirement: Governance assertions SHALL enforce single-entry bridge DX invariants in app-layer code
Governance checks SHALL include semantic invariants that detect prohibited app-layer bridge plumbing patterns (for example direct low-level bridge global access and duplicated readiness orchestration) while allowing explicitly-scoped framework/internal exception paths.

#### Scenario: App-layer bridge DX invariant passes
- **WHEN** template/sample app-layer code uses approved profile entrypoints for host bootstrap and web bridge initialization
- **THEN** governance assertions pass without requiring brittle source file shape assumptions

#### Scenario: Prohibited app-layer bridge plumbing fails deterministically
- **WHEN** governance detects prohibited direct low-level bridge plumbing in app-layer code outside approved exception paths
- **THEN** governance fails with deterministic diagnostics including invariant id, artifact path, and expected-vs-actual summary

#### Scenario: Official sample/template app-layer paths are strict no-exception scope
- **WHEN** governance evaluates official maintained sample/template app-layer source paths
- **THEN** prohibited bridge-plumbing invariants SHALL be enforced without exception allowlist bypass

### Requirement: Governance diagnostics SHALL include exception-scope metadata for single-entry invariants
When single-entry invariants rely on scoped exception allowlists, governance diagnostics SHALL include allowlist scope identity so maintainers can audit whether a failure is a policy violation or an exception drift.

#### Scenario: Diagnostic includes exception scope context
- **WHEN** a single-entry semantic assertion evaluates a path covered by an exception scope
- **THEN** emitted diagnostic payload includes the evaluated scope identity and decision outcome

### Requirement: Single-entry DX governance SHALL run as an enforcing gate for official sample/template migration
After this change, semantic single-entry DX assertions for official maintained sample/template app-layer paths SHALL run in enforcing mode in CI governance, not warning-only mode.

#### Scenario: Enforcing gate blocks non-compliant official sample migration
- **WHEN** an official maintained sample/template app-layer path violates single-entry DX invariants
- **THEN** governance gate SHALL fail the CI run with deterministic invariant diagnostics

