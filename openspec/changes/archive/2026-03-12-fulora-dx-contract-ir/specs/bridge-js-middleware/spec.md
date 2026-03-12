## MODIFIED Requirements

### Requirement: withErrorNormalization middleware wraps RPC errors
The package SHALL provide a `withErrorNormalization()` built-in middleware that wraps raw JSON-RPC errors into typed `BridgeError` instances and SHALL preserve structured error fields (`code`, `message`, and optional `data`) whenever present in bridge responses.

Middleware-level error normalization SHALL support deterministic global handling hooks so applications can implement centralized retry, reporting, and user-facing error strategies without per-call ad-hoc catch blocks.

#### Scenario: RPC error is wrapped in BridgeError
- **WHEN** `withErrorNormalization()` is registered and a bridge call returns a JSON-RPC error
- **THEN** the error SHALL be wrapped in a `BridgeError` with `code`, `message`, and optional `data` properties

#### Scenario: Structured bridge error fields are preserved
- **WHEN** bridge response includes error `code`, `message`, and diagnostic `data`
- **THEN** normalized error objects SHALL retain those values without degrading to message-only strings

#### Scenario: Global error hook observes normalized errors
- **WHEN** normalized bridge errors are produced in middleware pipeline
- **THEN** configured global error hooks SHALL receive normalized error context before the error is rethrown to the caller

#### Scenario: Non-RPC errors pass through unchanged
- **WHEN** `withErrorNormalization()` is registered and a non-RPC error occurs (for example transport/runtime failure)
- **THEN** the original error SHALL propagate without wrapping
