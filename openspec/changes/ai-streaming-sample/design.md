## Context

Fulora's bridge streaming infrastructure (`IAsyncEnumerable<T>` → JS `AsyncIterable<T>`) was delivered in Phase 8 (M8.3). The protocol uses token-based pull: initial response returns `{ token, values? }`, JS calls `$/enumerator/next/{token}` to pull items, and `$/enumerator/abort/{token}` to cancel. `CancellationToken` maps to JS `AbortSignal`. This change creates a showcase AI chat app and fixes a gap: the 30-second enumerator inactivity timeout from the spec is not implemented.

**Existing contracts**: `IAsyncEnumerable<T>` bridge support, `IBridgeEvent<T>` for push events, `CancellationToken` → `AbortSignal`, `WebViewRpcService.RegisterEnumerator`.

## Goals / Non-Goals

**Goals:**
- Sample app: `samples/avalonia-ai-chat` with Avalonia desktop host + React chat UI
- `IAiChatService` (`[JsExport]`) with `IAsyncEnumerable<string> StreamCompletion(string prompt, CancellationToken ct)`
- C# implementation using `Microsoft.Extensions.AI` `IChatClient` abstraction
- Configurable backend: Ollama (local, no API key), OpenAI, Azure OpenAI
- React chat UI with real-time token rendering
- Documentation: `docs/ai-integration-guide.md`
- Fix: implement enumerator inactivity timeout (30s) in `WebViewRpcService`

**Non-Goals:**
- General AI SDK or plugin
- RAG, embeddings, or vector search
- Multi-turn conversation state persistence
- Prompt template library

## Decisions

### D1: Use Microsoft.Extensions.AI as LLM abstraction

**Choice**: `Microsoft.Extensions.AI` (`IChatClient`) as the C# backend abstraction. Support `OllamaChatClient` as default (local, free, no API key) with documented swap to `OpenAIChatClient`.

**Rationale**: Microsoft.Extensions.AI is the official .NET LLM abstraction (GA in .NET 9+). Provider-agnostic. Aligns with .NET ecosystem. Ollama default means zero-cost local development.

### D2: Sample structure follows existing conventions

**Choice**: `samples/avalonia-ai-chat/` with:
- `AvaloniAiChat.Desktop/` — Avalonia host
- `AvaloniAiChat.Bridge/` — `[JsExport]` interfaces
- `AvaloniAiChat.Web/` — React + Vite frontend

**Rationale**: Matches `avalonia-react` sample structure. Developers can copy-paste.

### D3: Enumerator inactivity timeout via CancellationTokenSource

**Choice**: Add a per-enumerator `CancellationTokenSource` with 30s timeout. Reset on each `$/enumerator/next` call. On timeout, dispose the enumerator and remove from `_activeEnumerators`.

**Rationale**: Prevents resource leak from abandoned enumerators. Spec already defines 30s; implementation was deferred.

### D4: "User is typing" via bridge RPC notification

**Choice**: JS calls a lightweight `[JsExport]` method `NotifyTyping(bool isTyping)` on the chat service. No `IBridgeEvent` needed (that's C#→JS only).

**Rationale**: Simple, type-safe, uses existing bridge call mechanism. No new infrastructure needed.

## Risks / Trade-offs

- **[Risk] Ollama availability** → Document installation steps; sample works without AI backend by falling back to echo mode.
- **[Trade-off] Microsoft.Extensions.AI maturity** → GA since .NET 9; stable enough for a sample.
- **[Trade-off] No persistent conversation** → Keeps sample focused on streaming pattern, not storage.

## Testing Strategy

- **CT**: Enumerator inactivity timeout behavior in `WebViewRpcService` unit tests
- **CT**: `IAiChatService` contract test with mock `IChatClient`
- **IT**: Manual validation of sample app streaming flow
