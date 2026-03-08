## Purpose

Specifies the Ollama provider NuGet package (`Agibuild.Fulora.AI.Ollama`) and its integration with the Fulora AI provider registry, including middleware support.

## Requirements

### Requirement: Ollama provider NuGet package

The system SHALL provide `Agibuild.Fulora.AI.Ollama` as a separate NuGet package that registers an Ollama-backed `IChatClient` with the Fulora AI provider registry.

#### Scenario: Register Ollama provider with default endpoint
- **WHEN** developer calls `ai.AddOllama("local")` without specifying an endpoint
- **THEN** the system SHALL register an `IChatClient` named "local" that connects to `http://localhost:11434`

#### Scenario: Register Ollama provider with custom endpoint and model
- **WHEN** developer calls `ai.AddOllama("local", new Uri("http://gpu-host:11434"), "llama3")`
- **THEN** the system SHALL register an `IChatClient` named "local" that connects to the specified endpoint with the specified model

#### Scenario: Ollama provider streams responses
- **GIVEN** an Ollama provider is registered and the Ollama server is reachable
- **WHEN** `IChatClient.GetStreamingResponseAsync` is called
- **THEN** the system SHALL delegate to `OllamaChatClient.GetStreamingResponseAsync` and yield response chunks

### Requirement: Ollama provider DI integration

The system SHALL integrate with `FuloraAiBuilder` so all middleware (resilience, metering, content gate) applies to Ollama calls.

#### Scenario: Middleware applies to Ollama provider
- **GIVEN** Ollama is registered and resilience middleware is enabled
- **WHEN** a transient error occurs during an Ollama call
- **THEN** the resilience middleware SHALL retry the call according to configured policy
