## MODIFIED Requirements

### Requirement: withErrorNormalization middleware wraps RPC errors
The package SHALL provide a `withErrorNormalization()` built-in middleware that wraps raw JSON-RPC errors into typed `BridgeError` instances.

Middleware-level error normalization SHALL preserve structured error fields (`code`, `message`, and optional `data`) whenever present and SHALL support deterministic global handling hooks for centralized retry/reporting strategy implementation.

#### Scenario: RPC error is wrapped in BridgeError
- **WHEN** `withErrorNormalization()` is registered and a bridge call returns a JSON-RPC error
- **THEN** the error SHALL be wrapped in a `BridgeError` with `code`, `message`, and optional `data` properties

#### Scenario: Global error hook observes normalized errors before rethrow
- **WHEN** normalized bridge errors are produced in middleware pipeline
- **THEN** configured global error hooks SHALL receive normalized error context before the error is rethrown to the caller

#### Scenario: Non-RPC errors pass through unchanged
- **WHEN** `withErrorNormalization()` is registered and a non-RPC error occurs (e.g., network failure)
- **THEN** the original error SHALL propagate without wrapping
