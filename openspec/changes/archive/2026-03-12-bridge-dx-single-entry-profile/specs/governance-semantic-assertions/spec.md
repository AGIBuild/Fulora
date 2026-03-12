## ADDED Requirements

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
