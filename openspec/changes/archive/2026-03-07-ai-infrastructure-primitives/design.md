## Context

Fulora (v1.1.0, Phase 12 complete) is a production-ready hybrid app framework with typed bridge, policy-first runtime, streaming (`IAsyncEnumerable` ↔ `AsyncIterable`), plugin system, and OpenTelemetry integration. AI capabilities are increasingly expected in desktop applications, but developers must currently build integration plumbing from scratch.

Microsoft has standardized AI abstractions via `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator<,>`). This provides the provider abstraction layer — Fulora should integrate with it, not replace it.

Existing Fulora patterns that map to AI concerns:
- `IBridgeMiddleware` → AI call middleware (resilience, metering, content gate)
- `IWebMessagePolicy` → AI safety policy
- Source Generator (`BridgeHostEmitter`, `TypeScriptEmitter`) → AI tool schema emitter
- `IBridgeTracer` + `CompositeTelemetryProvider` → AI telemetry
- `byte[]` Bridge payload → multimodal binary transport

## Goals / Non-Goals

**Goals:**
- Provide AI infrastructure primitives that are useful regardless of AI paradigm (agents, RAG, chat, etc.)
- Integrate with `Microsoft.Extensions.AI` abstractions — not invent new provider contracts
- Follow Fulora's existing patterns: DI registration, middleware pipeline, policy gate, source generation
- Maintain G4 (Contract-Driven Testability) — all AI primitives testable via mocks without real LLM
- Expose AI capabilities to JS via typed bridge, preserving G1 (Type-Safe Bridge)

**Non-Goals:**
- Agent engine / orchestration framework (use Semantic Kernel or build at app layer)
- MCP server/client hosting (CLI/API/Skills already serve this purpose)
- RAG pipeline or vector store (application-layer concern)
- Specific AI provider implementations (shipped as separate NuGet packages)
- Prompt engineering tooling or prompt library
- Model management, fine-tuning, or training

## Decisions

### D1: Package structure — single `Agibuild.Fulora.AI` package

**Choice**: One new NuGet package `Agibuild.Fulora.AI` containing all 7 primitives.

**Alternatives considered**:
- Separate packages per primitive (e.g., `Fulora.AI.Resilience`, `Fulora.AI.Metering`) — too granular, adds dependency management overhead for users
- Everything in `Agibuild.Fulora.Core` — pollutes the core package with AI-specific types

**Rationale**: The 7 primitives are cohesive (all relate to AI call lifecycle) and lightweight. A single package with `Microsoft.Extensions.AI.Abstractions` as its only hard dependency keeps things simple. Provider packages (Ollama, OpenAI, etc.) are separate.

### D2: Resilience — Polly v8 via `Microsoft.Extensions.Resilience`

**Choice**: Use Polly v8 (`Microsoft.Extensions.Resilience`) for retry, timeout, circuit breaker, and rate limiter.

**Alternatives considered**:
- Custom middleware decorators — reinventing Polly poorly
- No built-in resilience — every app repeats the same patterns

**Rationale**: Polly is the .NET standard for resilience. `Microsoft.Extensions.Resilience` integrates with DI and is used by `Microsoft.Extensions.Http.Resilience`. The AI middleware wraps `IChatClient` as a delegating decorator, same pattern as `HttpClient` resilience.

### D3: Token metering — middleware decorator on `IChatClient`

**Choice**: Implement as an `IChatClient` decorator that intercepts `CompleteAsync`/`CompleteStreamingAsync`, extracts `Usage` from responses, and emits metrics.

**Rationale**: `Microsoft.Extensions.AI` responses include `Usage` (prompt/completion tokens). The decorator pattern matches how `IChatClient` is designed for composition. Budget enforcement is a policy check after usage extraction.

Metrics emitted via:
- `IBridgeTracer` (existing Fulora tracer)
- `System.Diagnostics.Metrics` (for OpenTelemetry export via existing `Agibuild.Fulora.Telemetry.OpenTelemetry` package)

### D4: Structured output — source generator emits JSON Schema from C# types

**Choice**: Extend the existing source generator to emit JSON Schema from C# record/class types used as AI response types. Runtime validates response and retries with error feedback.

**Flow**:
1. Developer declares: `var result = await ai.CompleteAsync<OrderSummary>(prompt);`
2. Source generator has already emitted `OrderSummaryJsonSchema` constant
3. Runtime appends `response_format: { type: "json_schema", schema: ... }` to the request
4. Response is deserialized and validated against schema
5. If invalid: retry with the original prompt + validation error (up to N times, configurable, default 3)
6. If all retries fail: throw `AiStructuredOutputException` with last validation error

**Alternatives considered**:
- Runtime reflection-based schema generation — breaks NativeAOT
- No retry — poor developer experience with non-deterministic LLM output

### D5: Content safety — `IAiContentFilter` pipeline, not hardcoded rules

**Choice**: Define `IAiContentFilter` interface with `FilterInputAsync` / `FilterOutputAsync` methods. Multiple filters compose into a pipeline via DI. No built-in filters — provide the extension point.

**Rationale**: Content safety rules are application-specific and culturally dependent. Fulora provides the pipeline and invocation point (before sending to LLM, after receiving response). Applications or community packages provide the actual filter logic (PII detection, keyword blocking, classifier-based filtering).

### D6: Tool schema generation — opt-in `[AiTool]` attribute on `[JsExport]` interfaces

**Choice**: New `[AiTool]` attribute. When a `[JsExport]` interface or method has `[AiTool]`, the source generator emits an additional OpenAI-compatible function tool JSON Schema.

**Alternatives considered**:
- Auto-generate for all `[JsExport]` — too aggressive, exposes internal APIs to AI
- Separate attribute without `[JsExport]` — loses the typed bridge integration benefit

**Rationale**: Opt-in is safer and aligned with G3 (Secure by Default). The schema reuses existing source generator infrastructure (type extraction, JSON serialization context). XML doc comments become tool descriptions.

### D7: Multimodal payload — extend existing `app://` scheme handler

**Choice**: Add chunked binary upload via `app://ai/upload/{id}` scheme endpoint. JS sends `File`/`Blob` via `fetch()` to this URL; C# receives a `Stream`. For download (C# → JS), generate a temporary `app://ai/blob/{id}` URL that JS can `fetch()`.

**Alternatives considered**:
- Base64 over Bridge — 33% overhead, memory pressure for large files
- New WebSocket channel — added complexity, not needed for request/response pattern

**Rationale**: The `app://` scheme handler is already used for SPA hosting. Extending it for binary AI payloads avoids Base64 overhead and reuses existing infrastructure. Content-Type header carries MIME type.

## Risks / Trade-offs

- **[Risk] `Microsoft.Extensions.AI` API instability** → Pin to stable release version; abstract behind thin Fulora interfaces where needed for forward compatibility
- **[Risk] Source generator complexity for JSON Schema** → Start with flat types (records with primitive/string/enum/collection properties); explicitly skip nested generics and emit diagnostic `AGBR008`
- **[Risk] Streaming + metering interaction** → Streaming responses emit token counts only at stream completion; mid-stream budget enforcement is not practical — enforce budget pre-flight based on estimated prompt tokens
- **[Risk] Content filter latency** → Filters run in the hot path; document performance expectations; provide async filter with cancellation
- **[Trade-off] No built-in content filters** → Lower out-of-box value, but avoids opinionated filtering that would be wrong for many use cases
- **[Trade-off] Polly dependency** → Adds ~200KB to package; justified by maturity and .NET ecosystem alignment

## Testing Strategy

- **Contract tests (CT)**: All 7 primitives tested via mock `IChatClient` / mock filters / mock tracers. No real LLM calls in CI.
- **Source generator tests**: Verify `[AiTool]` emits correct JSON Schema for representative types (flat record, enum, nullable, collection, nested object).
- **Integration tests (IT)**: End-to-end test with in-process mock LLM server → resilience middleware → metering → content gate → structured output validation.
- **MockAdapter compatibility**: AI services exposed via Bridge are testable through existing `MockBridge<T>` pattern.
