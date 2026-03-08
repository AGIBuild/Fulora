## Purpose

Specifies streaming chat completion support on the AI bridge service, exposing real-time token streaming to JavaScript via `IAsyncEnumerable<string>` and TypeScript `AsyncIterable<string>`.

## Requirements

### Requirement: Streaming chat completion on AI bridge service

`IAiBridgeService` SHALL expose a `StreamCompletion` method that returns `IAsyncEnumerable<string>` for real-time token streaming to JavaScript.

#### Scenario: JS calls streaming chat completion
- **WHEN** JS calls `aiBridgeService.streamCompletion(request)` via the bridge
- **THEN** the system SHALL invoke `IChatClient.GetStreamingResponseAsync` on the resolved provider
- **AND** each yielded string SHALL contain one or more text tokens from the LLM response
- **AND** the async iterable SHALL complete when the LLM response finishes

#### Scenario: Streaming respects cancellation
- **GIVEN** a streaming completion is in progress
- **WHEN** JS aborts the stream via `AbortController`
- **THEN** the C# `CancellationToken` SHALL be cancelled
- **AND** the `IAsyncEnumerable` SHALL stop yielding and dispose the enumerator

#### Scenario: Streaming goes through middleware pipeline
- **GIVEN** content gate and metering middleware are registered
- **WHEN** a streaming completion is invoked
- **THEN** the content gate SHALL filter input messages before streaming
- **AND** the metering middleware SHALL record token usage from the streaming response

#### Scenario: Streaming with no provider returns error
- **WHEN** `StreamCompletion` is called with no providers registered
- **THEN** the system SHALL throw an `InvalidOperationException`
- **AND** the bridge SHALL return a JSON-RPC error to JS

### Requirement: TypeScript types include streaming method

`packages/bridge-ai` SHALL export TypeScript types for the streaming completion method.

#### Scenario: TypeScript client exposes streamCompletion
- **WHEN** a TypeScript consumer imports `IAiBridgeService` types
- **THEN** `streamCompletion(request: AiChatRequest)` SHALL be typed as returning `AsyncIterable<string>`
