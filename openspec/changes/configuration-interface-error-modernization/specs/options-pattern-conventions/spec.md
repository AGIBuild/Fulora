## ADDED Requirements

### Requirement: AI options use IOptions pattern
AI configuration options (AiResilienceOptions, AiMeteringOptions, AiToolCallingOptions, AiConversationOptions) SHALL be registered via `AddOptions<T>().ValidateDataAnnotations()` and injected as `IOptions<T>`.

#### Scenario: Options validation on startup
- **WHEN** invalid AI options are configured (e.g., negative retry count)
- **THEN** the DI container MUST throw OptionsValidationException at first resolution

#### Scenario: Fluent builder backward compatibility
- **WHEN** FuloraAiBuilder fluent API sets options values
- **THEN** those values MUST be applied to the underlying IOptions registration

### Requirement: HttpClient factory for remote config
AddRemoteConfig SHALL use IHttpClientFactory to obtain HttpClient instances. Direct `new HttpClient()` construction is forbidden.

#### Scenario: Remote config HttpClient resolution
- **WHEN** AddRemoteConfig is called
- **THEN** it MUST resolve an HttpClient via IHttpClientFactory using a named client "FuloraRemoteConfig"
