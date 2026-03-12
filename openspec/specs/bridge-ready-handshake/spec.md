# bridge-ready-handshake Specification

## Purpose
TBD - created by archiving change fulora-dx-contract-ir. Update Purpose after archive.
## Requirements
### Requirement: Bridge readiness SHALL use sticky handshake semantics
Bridge readiness SHALL be represented by a sticky state and a ready notification event. Clients SHALL be able to determine readiness correctly regardless of whether they subscribe before or after host initialization.

#### Scenario: Late subscriber resolves immediately from sticky state
- **WHEN** the bridge is already initialized before client readiness subscription
- **THEN** readiness APIs SHALL resolve using sticky state without waiting for a new event

#### Scenario: Early subscriber resolves from event
- **WHEN** a client subscribes before the bridge becomes ready
- **THEN** readiness APIs SHALL resolve when the ready event is emitted

### Requirement: Polling SHALL be optional fallback, not primary readiness contract
Polling-based readiness checks MAY exist as compatibility fallback, but the normative readiness path SHALL be handshake-based and race-safe.

#### Scenario: Handshake path succeeds without polling loop
- **WHEN** bridge host and web client both support handshake semantics
- **THEN** readiness SHALL complete without repeated polling intervals

#### Scenario: Timeout remains observable and actionable
- **WHEN** readiness is not achieved within configured timeout
- **THEN** readiness APIs SHALL reject with deterministic timeout semantics suitable for UI retry/error handling

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

