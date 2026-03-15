## MODIFIED Requirements

### Requirement: AddFulora uses Options pattern internally
AddFulora() and its builder extensions SHALL register configuration objects via the IOptions pattern internally. The fluent builder API surface (AddJsonFileConfig, AddRemoteConfig, AddTelemetry, AddAutoUpdate, ConfigureBridge) MUST remain unchanged. Options types MUST support binding from IConfiguration sections.

#### Scenario: Fluent API preserved
- **WHEN** existing consumer code calls services.AddFulora().AddRemoteConfig(uri)
- **THEN** it MUST compile and work identically to current behavior

#### Scenario: Configuration binding available
- **WHEN** a consumer binds options from IConfiguration
- **THEN** options MUST be populatable via configuration.GetSection("Fulora:xxx")
