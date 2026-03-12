## ADDED Requirements

### Requirement: Readiness profile SHALL use handshake-first semantics as normative behavior
The readiness profile API SHALL resolve bridge readiness using sticky state plus ready event semantics before any compatibility polling behavior.

#### Scenario: Late subscriber resolves from sticky state
- **WHEN** readiness profile API is called after bridge initialization completed
- **THEN** readiness SHALL resolve immediately using sticky state without waiting for another event

#### Scenario: Early subscriber resolves from ready event
- **WHEN** readiness profile API is called before bridge initialization completes
- **THEN** readiness SHALL resolve when the ready event is emitted

### Requirement: Compatibility polling SHALL be explicit fallback-only behavior
Polling-based readiness checks MAY exist for compatibility, but they SHALL be fallback behavior behind handshake-first logic and SHALL preserve deterministic timeout semantics.

#### Scenario: Handshake path succeeds without polling loop
- **WHEN** host and client both support handshake semantics
- **THEN** readiness SHALL complete without repeated polling intervals

#### Scenario: Timeout remains deterministic for fallback path
- **WHEN** readiness is not achieved within configured timeout
- **THEN** readiness SHALL fail with deterministic timeout semantics suitable for retry/error handling

### Requirement: Profile readiness API SHALL be the default sample/template entrypoint
Template and maintained sample app layers SHALL consume readiness via the profile API rather than ad-hoc direct bridge polling loops.

#### Scenario: Template app avoids ad-hoc polling loop
- **WHEN** generated template web source is inspected
- **THEN** bridge readiness behavior SHALL be wired through profile readiness API and SHALL NOT rely on app-layer requestAnimationFrame polling loops
