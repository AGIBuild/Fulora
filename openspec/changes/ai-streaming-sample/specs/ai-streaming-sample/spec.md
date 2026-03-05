## Purpose

Define requirements for the AI streaming sample app and the enumerator inactivity timeout fix. Ensures the sample demonstrates real-world LLM streaming patterns and the bridge handles abandoned enumerators gracefully.

## ADDED Requirements

### Requirement: IAiChatService exposes streaming completion via IAsyncEnumerable

`IAiChatService` SHALL be a `[JsExport]` interface with a streaming completion method.

#### Scenario: Streaming chat completion returns tokens one by one
- **GIVEN** an `IAiChatService` implementation backed by an `IChatClient`
- **WHEN** `StreamCompletion("Hello, how are you?")` is invoked from JS
- **THEN** the bridge SHALL return an `AsyncIterable<string>` to JS
- **AND** each iteration SHALL yield one or more text tokens
- **AND** the iterable SHALL complete when the LLM response is finished

#### Scenario: Cancellation aborts the stream
- **GIVEN** a streaming completion in progress
- **WHEN** the JS consumer calls `abort()` on the `AbortController`
- **THEN** the C# `CancellationToken` SHALL be cancelled
- **AND** the `IAsyncEnumerable` SHALL stop yielding
- **AND** the enumerator SHALL be disposed

#### Scenario: Backend unavailable returns error
- **GIVEN** no LLM backend is configured or reachable
- **WHEN** `StreamCompletion(prompt)` is invoked
- **THEN** the bridge SHALL return a JSON-RPC error
- **AND** the error SHALL include a descriptive message (e.g., "AI backend not available")

### Requirement: Enumerator inactivity timeout disposes abandoned enumerators

`WebViewRpcService` SHALL dispose enumerators that are not polled within 30 seconds.

#### Scenario: Enumerator times out after 30 seconds of inactivity
- **GIVEN** an active `IAsyncEnumerable` enumerator registered in `_activeEnumerators`
- **WHEN** no `$/enumerator/next/{token}` call is received for 30 seconds
- **THEN** the enumerator SHALL be disposed
- **AND** the handler SHALL be removed from `_handlers`
- **AND** subsequent `$/enumerator/next/{token}` calls SHALL return `{ values: [], finished: true }`

#### Scenario: Polling resets the inactivity timer
- **GIVEN** an active enumerator with 25 seconds since last poll
- **WHEN** `$/enumerator/next/{token}` is received
- **THEN** the inactivity timer SHALL reset to 30 seconds
- **AND** the enumerator SHALL remain active

### Requirement: React chat UI renders streaming tokens in real-time

The React web UI SHALL render streaming tokens incrementally as they arrive from the bridge, providing a responsive chat experience.

#### Scenario: Tokens appear incrementally in the UI
- **GIVEN** the chat UI is open and the user submits a prompt
- **WHEN** the bridge begins streaming tokens
- **THEN** each token SHALL appear in the message area as it arrives
- **AND** the message SHALL not wait for the full response before displaying

#### Scenario: User can cancel an in-progress response
- **GIVEN** a streaming response is in progress
- **WHEN** the user clicks the "Stop" button
- **THEN** the stream SHALL be aborted via `AbortController`
- **AND** the partial response SHALL remain visible
- **AND** the UI SHALL return to the input-ready state

### Requirement: Sample works without API key via echo/Ollama fallback

The sample SHALL function without any external AI service by falling back to an echo-mode implementation.

#### Scenario: No AI backend configured
- **GIVEN** no `AI__Provider` environment variable is set
- **WHEN** the sample app starts
- **THEN** the app SHALL use an echo-mode implementation that streams back the prompt character by character
- **AND** a banner SHALL inform the user that AI is in demo mode
