## Purpose

Specifies DI registration, default provider resolution, bridge exposure of AI chat to JavaScript, and middleware pipeline composition for AI provider integration.

## Requirements

### Requirement: AI provider DI registration
The system SHALL provide `AddFuloraAi()` extension method on `IServiceCollection` that accepts a builder delegate for configuring AI providers.

#### Scenario: Register a single chat provider
- **WHEN** developer calls `services.AddFuloraAi(ai => ai.AddChatClient("default", chatClient))`
- **THEN** an `IChatClient` instance is resolvable from DI with the name "default"

#### Scenario: Register multiple named providers
- **WHEN** developer registers providers named "fast" and "smart"
- **THEN** `IAiProviderRegistry.GetChatClient("fast")` returns the fast provider and `GetChatClient("smart")` returns the smart provider

### Requirement: Default provider resolution
The system SHALL resolve the first registered provider as the default when no name is specified.

#### Scenario: Resolve default provider
- **WHEN** developer calls `IAiProviderRegistry.GetChatClient()` without a name
- **THEN** the system returns the first registered `IChatClient`

#### Scenario: No providers registered
- **WHEN** developer calls `IAiProviderRegistry.GetChatClient()` with no providers registered
- **THEN** the system throws `InvalidOperationException` with a descriptive message

### Requirement: Bridge exposure of AI chat
The system SHALL expose AI chat capabilities to JavaScript via the typed bridge, supporting both single-response and streaming modes.

#### Scenario: JS calls chat completion
- **WHEN** JS calls `aiChat.complete(messages)` via bridge
- **THEN** the system invokes `IChatClient.GetResponseAsync` on the resolved provider and returns the response through the bridge

#### Scenario: JS calls streaming chat completion
- **WHEN** JS calls `aiChat.streamCompletion(request)` via bridge
- **THEN** the system invokes `IChatClient.GetStreamingResponseAsync` and streams `IAsyncEnumerable<string>` chunks to JS as `AsyncIterable<string>`

#### Scenario: JS lists available providers
- **WHEN** JS calls `aiChat.listProviders()` via bridge
- **THEN** the system returns an array of registered provider names

### Requirement: Middleware pipeline composition
The system SHALL compose registered middleware (resilience, metering, content gate) as `IChatClient` decorator chain via DI.

#### Scenario: Middleware ordering
- **WHEN** resilience, metering, and content filter middleware are all registered
- **THEN** the call chain is: Content Gate (input) → Resilience → Metering → Provider → Metering (record) → Content Gate (output)
