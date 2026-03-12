## ADDED Requirements

### Requirement: Framework SHALL provide deterministic host bootstrap orchestration
The framework SHALL provide a host bootstrap API that encapsulates SPA navigation mode selection (dev/prod), bridge script availability, service exposure ordering, and lifecycle wiring, so app hosts do not need to handcraft per-window orchestration logic.

#### Scenario: Bootstrap selects dev navigation flow
- **WHEN** bootstrap is executed in development mode with a configured dev server endpoint
- **THEN** the framework SHALL navigate to the dev endpoint using the defined dev flow and preserve bridge availability semantics

#### Scenario: Bootstrap selects production SPA hosting flow
- **WHEN** bootstrap is executed in production mode with embedded resource hosting options
- **THEN** the framework SHALL enable SPA hosting and navigate to the production app URI with deterministic fallback behavior

### Requirement: Bootstrap SHALL guarantee bridge registration ordering and lifecycle ownership
Bridge service registration performed through bootstrap SHALL be applied in deterministic order after navigation preconditions are satisfied, and disposal/lifecycle hooks SHALL be framework-owned and idempotent.

#### Scenario: Registration order is deterministic
- **WHEN** multiple bridge services/plugins are configured for exposure
- **THEN** bootstrap SHALL expose them in a deterministic order that is stable across runs

#### Scenario: Framework-owned disposal prevents leaks
- **WHEN** the host window or runtime scope is disposed
- **THEN** bootstrap-managed bridge registrations and lifecycle bindings SHALL be released exactly once without requiring manual per-service dispose wiring
