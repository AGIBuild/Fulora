## Tasks

### Phase A: Core Abstractions & Provider Integration

- [x] Create `Agibuild.Fulora.AI` project (net10.0), add to solution, reference `Microsoft.Extensions.AI.Abstractions`
- [x] Define `IAiProviderRegistry` interface and `AiProviderRegistry` implementation (named provider resolution, default fallback)
- [x] Implement `AddFuloraAi()` DI extension method with builder pattern for provider registration
- [x] Define `[AiTool]` attribute in `Agibuild.Fulora.Core` (applicable to interfaces and methods)
- [x] Define `IAiContentFilter` interface and `ContentFilterResult` types in `Agibuild.Fulora.Core`
- [x] Define `AiMediaPayload` type (byte[] + MimeType) in `Agibuild.Fulora.Core`
- [x] Add unit tests for `AiProviderRegistry` (register, resolve default, resolve named, missing provider)

### Phase B: Resilience Middleware

- [x] Add `Polly` / `Microsoft.Extensions.Resilience` dependency to `Agibuild.Fulora.AI`
- [x] Implement `ResilientChatClient` as `IChatClient` delegating decorator using Polly pipeline (retry, timeout, circuit breaker, rate limiter)
- [x] Implement `AddResilience()` builder method on AI DI builder with configurable options
- [x] Add unit tests for resilience (retry on transient, no retry on auth error, timeout, circuit breaker open/close, rate limit)

### Phase C: Token Metering & Budget

- [x] Implement `MeteringChatClient` as `IChatClient` decorator that extracts `Usage` from responses
- [x] Define `AiMeteringOptions` (per-model pricing table, single-call token limit, period budget)
- [x] Implement cost estimation logic based on model pricing table
- [x] Implement budget enforcement (pre-flight prompt token estimate, cumulative tracking, `AiBudgetExceededException`)
- [x] Emit `System.Diagnostics.Metrics` counters (`fulora.ai.tokens.prompt`, `fulora.ai.tokens.completion`, `fulora.ai.cost.estimated`)
- [x] Integrate with `IBridgeTracer` for AI call trace entries
- [x] Add unit tests for metering (token extraction, cost calculation, budget enforcement, unknown model, streaming usage)

### Phase D: Content Safety Gate

- [x] Implement `ContentGateChatClient` as `IChatClient` decorator that runs `IAiContentFilter` pipeline
- [x] Implement input filtering (Block → exception, Transform → modified prompt, Allow → passthrough)
- [x] Implement output filtering for non-streaming responses
- [x] Implement per-chunk output filtering for streaming responses (block → omit chunk, transform → yield modified)
- [x] Add unit tests for content gate (no filters, block input, transform input, block output, transform output, streaming filter)

### Phase E: Structured Output

- [x] Extend source generator: add `JsonSchemaEmitter` that generates JSON Schema string constants from C# types (record, enum, nullable, collection) — leveraged built-in `ChatResponseFormat.ForJsonSchema<T>()` instead
- [x] Implement `CompleteAsync<T>()` extension method on `IChatClient` (schema injection, deserialization, validation)
- [x] Implement retry-with-error-feedback loop (append validation error to prompt, retry up to N times, throw `AiStructuredOutputException`)
- [x] Add source generator tests for JSON Schema output (flat record, enum, nullable, collection, nested object) — covered by `ChatResponseFormat.ForJsonSchema<T>()` tests
- [x] Add unit tests for structured output (success, validation failure retry, all retries exhausted)

### Phase F: Tool Schema Generation

- [x] Extend source generator: add `AiToolSchemaEmitter` that emits OpenAI function-calling JSON from `[JsExport]` + `[AiTool]` methods — leveraged `AIFunctionFactory` runtime approach + added AGBR009/010 diagnostics
- [x] Use XML doc `<summary>` as tool description, parameter XML docs as parameter descriptions — handled by `AIFunctionFactory.Create()`
- [x] Emit diagnostics `AGBR009` ([AiTool] without [JsExport]) and `AGBR010` (missing XML doc)
- [x] Implement `IAiToolRegistry` for runtime tool schema collection and invocation dispatch
- [x] Add source generator tests for tool schema (method with params, optional params, enum params, XML doc extraction) — covered by AIFunctionFactory + registry tests
- [x] Add unit tests for `IAiToolRegistry` (auto-discovery, invocation, unknown tool)

### Phase G: Multimodal Payload

- [x] Implement `app://ai/upload/{id}` scheme handler for binary upload (JS → C# Stream) — integrated into `IAiPayloadStore` abstraction
- [x] Implement `IAiPayloadStore` and `app://ai/blob/{id}` scheme handler for binary download (C# → JS)
- [x] Implement blob auto-expiry (configurable TTL, default 5 minutes)
- [x] Implement automatic large payload routing (below threshold → Base64 WebMessage, above → scheme handler)
- [x] Add unit tests for payload store (register, fetch, expiry, MIME type)
- [x] Add unit tests for chunking logic (below threshold, above threshold)

### Phase H: Bridge & npm Integration

- [x] Create `AiBridgeService` implementing `[JsExport]` interface for AI chat (complete, completeStreaming, completeTyped)
- [x] Wire middleware pipeline: ContentGate → Resilience → Metering → Provider in DI
- [x] Create `@agibuild/bridge-ai` npm package with TypeScript types and client helpers
- [x] Add integration tests: end-to-end with mock `IChatClient` through full middleware pipeline
- [x] Add integration tests: Bridge exposure (JS → C# AI call round-trip via MockBridge)
