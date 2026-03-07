## Context

`Agibuild.Fulora.AI` provides core AI abstractions: `IAiProviderRegistry`, resilience/metering middleware, structured output, content safety, and tool schema. However:

1. **No concrete providers** — developers must manually construct `IChatClient` instances and register them. No `dotnet add package` experience.
2. **No streaming on bridge** — `IAiBridgeService` only offers `Complete()` (request/response). The bridge already supports `IAsyncEnumerable` transport (Phase 8), but AI bridge doesn't use it.
3. **Sample bypasses Fulora.AI** — `avalonia-ai-chat` directly creates `OllamaChatClient`, skipping the provider registry, middleware pipeline, and DI integration.

This change makes the AI module usable end-to-end.

## Goals / Non-Goals

**Goals:**
- One-liner provider registration: `ai.AddOllama("local")`, `ai.AddOpenAI("cloud", apiKey)`
- Streaming AI completion via bridge: `IAiBridgeService.StreamCompletion()` → `IAsyncEnumerable<string>`
- Sample demonstrates the full Fulora.AI stack (DI → provider → middleware → bridge → React UI)
- Provider packages are separate NuGet packages with minimal dependency surface

**Non-Goals:**
- Providers beyond Ollama and OpenAI (Anthropic, Azure, etc. — future packages)
- Multi-turn conversation memory or chat history management
- Tool-calling orchestration runtime
- Embedding generator providers (chat-only for now)

## Decisions

### D1: Provider package structure — Thin extension method packages

Each provider package contains:
- A `FuloraAiBuilder` extension method (e.g., `AddOllama`, `AddOpenAI`)
- The upstream `Microsoft.Extensions.AI.*` package as a dependency
- No business logic — just wiring

**Why**: Keeps the dependency tree minimal. Apps that don't use OpenAI don't pull `Azure.AI.OpenAI`. Aligns with `Microsoft.Extensions.AI` ecosystem pattern.

**Alternative considered**: Single `Agibuild.Fulora.AI.Providers` mega-package → rejected (unnecessary dependency bloat).

### D2: Streaming via existing `IAsyncEnumerable` bridge transport

Add `IAsyncEnumerable<string> StreamCompletion(AiChatRequest request, CancellationToken ct)` to `IAiBridgeService`. The bridge source generator already handles `IAsyncEnumerable` → JS `AsyncIterable` transport (Phase 8 M8.3). Each yielded string is one token/chunk from the LLM.

**Why**: Zero new transport work; reuse proven infrastructure. Streaming granularity at text-token level matches all major LLM APIs.

**Alternative considered**: SSE/WebSocket side-channel → rejected (adds complexity; `IAsyncEnumerable` bridge transport already solves this).

### D3: Streaming middleware propagation

`StreamCompletion` goes through the same middleware pipeline: ContentGate filters input, then calls `IChatClient.GetStreamingResponseAsync`, ContentGate filters each output chunk. `MeteringChatClient` records usage from the final `StreamingChatCompletionUpdate` (usage is emitted in the last chunk by convention). `ResilientChatClient` applies retry to the initial connection, not per-chunk.

**Why**: Consistent behavior with non-streaming `Complete()`. Middleware is already implemented as `IChatClient` decorators that delegate to `GetStreamingResponseAsync`.

### D4: Sample upgrade — DI-based with env-var provider selection

Replace `MainWindow.CreateChatClient()` with DI setup:

```csharp
services.AddFuloraAi(ai =>
{
    ai.AddOllama("default", endpoint, model);
    // or ai.AddOpenAI("default", apiKey, model);
    ai.AddResilience();
    ai.AddMetering();
});
```

Provider selection driven by `AI__PROVIDER` env var (`ollama` | `openai`). Fallback to `EchoChatClient` when no provider is configured/reachable.

### D5: OpenAI provider — use `Microsoft.Extensions.AI.OpenAI`

The `Microsoft.Extensions.AI.OpenAI` package provides `OpenAIChatClient` implementing `IChatClient`. Our package wraps this with `FuloraAiBuilder.AddOpenAI()`.

**Why**: Official Microsoft package, actively maintained, consistent with the `Microsoft.Extensions.AI` abstraction layer.

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| `Microsoft.Extensions.AI.Ollama` may have breaking API changes | Pin to known-good version, wrap with thin adapter |
| `Microsoft.Extensions.AI.OpenAI` pulls `Azure.AI.OpenAI` transitive dependency (large) | Acceptable — only apps that `dotnet add` the OpenAI provider package take the hit |
| Streaming metering inaccuracy (usage only in last chunk) | Document that streaming metering is best-effort; recommend non-streaming for precise budget control |
| Sample env-var based provider selection is simplistic | Adequate for a sample; production apps use DI configuration |

## Testing Strategy

| Layer | Approach |
|-------|----------|
| Provider packages (Ollama, OpenAI) | Unit tests with mock `HttpClient` — verify client creation, option passing, DI registration |
| `StreamCompletion` bridge method | Unit test with mock `IChatClient` — verify `GetStreamingResponseAsync` delegation, chunk-by-chunk yield |
| Streaming middleware propagation | Integration test — ContentGate + Metering + mock provider → verify filtered streaming output |
| TypeScript types | Static — compile-time correctness via `tsc` |
| Sample integration | Manual smoke test — Ollama local / Echo fallback |
