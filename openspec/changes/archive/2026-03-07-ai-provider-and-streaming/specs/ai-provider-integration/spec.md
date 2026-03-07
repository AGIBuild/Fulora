## MODIFIED Requirements

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
