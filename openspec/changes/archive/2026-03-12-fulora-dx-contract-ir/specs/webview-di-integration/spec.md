## MODIFIED Requirements

### Requirement: AddFulora one-liner registration
The DI project SHALL provide an `AddFulora()` extension method that registers all framework services and SHALL define a first-class DI/plugin path for bridge service exposure as the recommended default integration model.

`AddFulora()` SHALL preserve explicit service ownership boundaries while enabling deterministic bridge exposure from DI-managed registrations without reflection-first assembly scanning.

#### Scenario: AddFulora registers core services
- **WHEN** a consumer calls `services.AddFulora()`
- **THEN** `IWebViewMessageBus`, `ITelemetryProvider`, and WebView factory SHALL be resolvable
- **AND** `ITelemetryProvider` SHALL default to `NullTelemetryProvider` when no custom provider is registered

#### Scenario: FuloraServiceBuilder provides fluent chaining
- **WHEN** `AddFulora()` returns a `FuloraServiceBuilder`
- **THEN** the builder SHALL support `.AddJsonFileConfig(path)`, `.AddRemoteConfig(uri)`, `.AddTelemetry(provider)`, and `.AddAutoUpdate(options, provider)` for optional service registration

#### Scenario: AddFulora does not override existing telemetry
- **WHEN** a custom `ITelemetryProvider` is registered before `AddFulora()`
- **THEN** `AddFulora()` SHALL NOT replace it with the default no-op provider

#### Scenario: DI-managed bridge exposure is deterministic
- **WHEN** bridge services are registered via plugin descriptors and resolved from service provider scope
- **THEN** bridge exposure order and lifecycle ownership SHALL be deterministic and repeatable across runs
