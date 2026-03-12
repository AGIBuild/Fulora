## ADDED Requirements

### Requirement: Fulora SHALL provide a profile-based single-entry DX API for bridge usage
The framework SHALL provide profile-level entry APIs so application-layer code uses one deterministic bridge startup path instead of ad-hoc bootstrap logic.

#### Scenario: App layer uses a single web bridge entrypoint
- **WHEN** a template or sample app initializes bridge access in web code
- **THEN** initialization SHALL go through the profile API instead of directly wiring readiness/middleware/mock logic in multiple files

#### Scenario: App layer uses a single host bridge entrypoint
- **WHEN** a template or sample app initializes host-side SPA + bridge startup
- **THEN** initialization SHALL go through the profile bootstrap entrypoint with deterministic ordering semantics

### Requirement: Profile web API SHALL be exported from `@agibuild/bridge/profile`
Profile-oriented web bootstrap APIs SHALL be provided from a dedicated package subpath and SHALL NOT require root-level profile exports for normative usage.

#### Scenario: Profile import path is explicit and stable
- **WHEN** template or sample web bridge bootstrap code imports profile APIs
- **THEN** imports SHALL resolve from `@agibuild/bridge/profile`
- **AND** app-layer normative usage SHALL NOT depend on root-level profile exports

### Requirement: Profile entry APIs SHALL encapsulate readiness, middleware, and mock-mode orchestration
Profile APIs SHALL own handshake-first readiness wiring, middleware baseline wiring, and standalone browser mock wiring so app-layer code does not duplicate those concerns.

#### Scenario: Profile entrypoint applies handshake-first readiness
- **WHEN** profile initialization runs in a host-backed environment
- **THEN** readiness SHALL resolve through sticky state/event semantics before compatibility fallback behavior

#### Scenario: Profile entrypoint enables deterministic standalone browser mode
- **WHEN** profile initialization runs in standalone browser mode without native host
- **THEN** generated mock contracts SHALL be installed before app mount and SHALL expose deterministic method identity and payload shape behavior

### Requirement: Profile architecture SHALL preserve low-level extensibility via explicit opt-in escape hatch
The framework SHALL allow advanced scenarios to bypass profile defaults only through explicit opt-in extension points, not implicit app-layer reimplementation.

#### Scenario: Advanced app uses explicit extension point
- **WHEN** an app requires custom low-level bridge behavior beyond profile defaults
- **THEN** the customization SHALL be declared through explicit profile extension hooks
- **AND** governance checks SHALL treat that usage as a tracked exception path

### Requirement: Official maintained samples SHALL migrate immediately from app-layer low-level direct RPC startup patterns
Official maintained samples that currently use app-layer direct low-level RPC startup/ready orchestration SHALL migrate to profile entrypoints within this change scope.

#### Scenario: Complex sample no longer uses app-layer low-level startup pattern
- **WHEN** maintained complex sample app-layer startup code is inspected after this change
- **THEN** startup and readiness SHALL use profile entrypoints
- **AND** app-layer direct low-level bridge startup orchestration SHALL be absent
