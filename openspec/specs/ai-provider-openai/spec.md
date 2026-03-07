## ADDED Requirements

### Requirement: OpenAI provider NuGet package

The system SHALL provide `Agibuild.Fulora.AI.OpenAI` as a separate NuGet package that registers an OpenAI-backed `IChatClient` with the Fulora AI provider registry.

#### Scenario: Register OpenAI provider with API key
- **WHEN** developer calls `ai.AddOpenAI("cloud", apiKey)`
- **THEN** the system SHALL register an `IChatClient` named "cloud" that authenticates with the provided API key

#### Scenario: Register OpenAI provider with custom model
- **WHEN** developer calls `ai.AddOpenAI("cloud", apiKey, "gpt-4o")`
- **THEN** the system SHALL register an `IChatClient` named "cloud" configured for the specified model

#### Scenario: Register OpenAI provider with custom endpoint
- **WHEN** developer calls `ai.AddOpenAI("cloud", apiKey, endpoint: new Uri("https://custom.openai.azure.com/"))`
- **THEN** the system SHALL register an `IChatClient` that routes requests to the custom endpoint

### Requirement: OpenAI provider DI integration

The system SHALL integrate with `FuloraAiBuilder` so all middleware applies to OpenAI calls.

#### Scenario: Middleware applies to OpenAI provider
- **GIVEN** OpenAI is registered and metering middleware is enabled
- **WHEN** a chat completion is invoked
- **THEN** the metering middleware SHALL record token usage from the OpenAI response
