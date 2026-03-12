## Purpose
Define dependency-injection integration contracts for WebView adapter factory registration.
## Requirements
### Requirement: Dependency injection project
The solution SHALL include a project named `Agibuild.Fulora.DependencyInjection` targeting `net10.0`.
The project SHALL reference `Agibuild.Fulora.Core` and `Agibuild.Fulora.Adapters.Abstractions`.
The project SHALL depend only on `Microsoft.Extensions.DependencyInjection.Abstractions` for DI APIs and SHALL NOT reference any platform adapter projects.

#### Scenario: DI project is platform-agnostic
- **WHEN** the DI project is built
- **THEN** it compiles without any platform-specific adapter dependencies

### Requirement: IServiceCollection extension entrypoint
The DI project SHALL provide an extension method:
`IServiceCollection AddWebView(this IServiceCollection services, Func<IServiceProvider, IWebViewAdapter> adapterFactory)`
The method SHALL register the `adapterFactory` so it can be resolved as a factory delegate (NOT as a shared `IWebViewAdapter` instance).
The DI integration SHALL ensure each WebView instance can obtain a fresh adapter instance via the registered factory.

#### Scenario: Adapter factory can be registered
- **WHEN** a consumer calls `AddWebView` with a factory delegate
- **THEN** the factory delegate can be resolved from the service provider and used to create an `IWebViewAdapter`

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

