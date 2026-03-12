## ADDED Requirements

### Requirement: Host bootstrap profile SHALL provide deterministic dev/prod orchestration
The host profile bootstrap API SHALL encapsulate development dev-server navigation and production embedded-resource hosting in one deterministic contract.

#### Scenario: Development profile selects dev-server path
- **WHEN** host profile bootstrap is executed in development mode with configured dev server URL
- **THEN** the WebView SHALL navigate via the dev profile path and preserve bridge availability semantics

#### Scenario: Production profile selects embedded hosting path
- **WHEN** host profile bootstrap is executed in production mode with embedded hosting configuration
- **THEN** SPA hosting SHALL be enabled and navigation SHALL use deterministic production URI behavior

### Requirement: Host bootstrap profile SHALL own bridge registration ordering and lifecycle
Bridge service registration through host profile bootstrap SHALL be applied in deterministic order after navigation preconditions are met, and profile-owned lifecycle disposal SHALL be idempotent.

#### Scenario: Registration order is stable across runs
- **WHEN** multiple bridge registrations are configured through the host profile
- **THEN** registration order SHALL remain stable across repeated runs with the same inputs

#### Scenario: Profile-owned teardown releases bridge resources exactly once
- **WHEN** the host window/runtime scope is disposed
- **THEN** profile-managed bridge registrations and lifecycle bindings SHALL be released exactly once without manual per-service disposal wiring

### Requirement: Host bootstrap profile SHALL support DI/plugin-first bridge registration composition
Host bootstrap profile SHALL compose DI-registered bridge configuration actions with explicit per-window bridge configuration in deterministic order.

#### Scenario: DI actions execute before explicit app configuration
- **WHEN** both DI bridge actions and explicit host bridge actions are configured
- **THEN** DI actions SHALL execute first and explicit actions SHALL execute afterward in deterministic order
