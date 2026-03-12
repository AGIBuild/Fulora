## ADDED Requirements

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
