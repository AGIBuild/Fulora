## Why

AI is becoming a baseline capability for desktop applications. Fulora's typed bridge, policy-first runtime, and streaming infrastructure (`IAsyncEnumerable`) provide strong foundations, but developers currently must build AI integration plumbing from scratch: provider management, call resilience, token budgeting, structured output enforcement, content safety, tool schema generation, and efficient multimodal transport. These are cross-cutting infrastructure concerns that belong in the framework, not in every application.

This change focuses on **infrastructure primitives** (not application-layer constructs like agent engines or RAG pipelines), ensuring long-term value regardless of how AI paradigms evolve. It extends existing Fulora patterns — Bridge Middleware, Policy, Source Generator, Telemetry — into the AI domain.

## What Changes

- Add `Agibuild.Fulora.AI` package with AI provider DI integration layer wrapping `Microsoft.Extensions.AI` (`IChatClient` / `IEmbeddingGenerator`)
- Add AI call resilience middleware pipeline (retry with exponential backoff, timeout, circuit breaker, rate limiter) as `IChatClient` decorators
- Add token metering middleware that tracks prompt/completion tokens, estimates cost, and enforces configurable budgets — outputs to existing `IBridgeTracer` / OpenTelemetry
- Add structured output support: C# type → JSON Schema generation (via source generator), response validation, and automatic retry-with-error-feedback loop (max N attempts)
- Add content safety gate (`IAiContentFilter` pipeline) for input/output filtering, extending the existing `IWebMessagePolicy` pattern
- Extend `Agibuild.Fulora.Bridge.Generator` to emit OpenAI-compatible function/tool JSON Schema from `[JsExport]` methods marked with opt-in `[AiTool]` attribute
- Enhance Bridge binary payload transport for multimodal AI use cases: chunked transfer for large payloads, MIME type annotation, and `app://` scheme handler path for File/Blob → Stream zero-copy

## Capabilities

### New Capabilities
- `ai-provider-integration`: DI registration, multi-provider routing, and Bridge exposure of `IChatClient` for JS consumption
- `ai-call-resilience`: Middleware pipeline (retry, timeout, circuit breaker, rate limit) for AI provider calls
- `ai-token-metering`: Per-call token tracking, cost estimation, budget enforcement, and telemetry export
- `ai-structured-output`: C# type → JSON Schema generation, LLM response validation, auto-retry with error feedback
- `ai-content-safety`: Input/output content filtering pipeline with pluggable `IAiContentFilter` providers
- `ai-tool-schema-generation`: Source generator extension to emit function-calling tool schemas from `[JsExport]` + `[AiTool]`
- `ai-multimodal-payload`: Chunked binary transfer, MIME annotation, and scheme-handler streaming for large/multimodal payloads

### Modified Capabilities
- `bridge-binary-payload`: Extended with chunked transfer and MIME type metadata for multimodal AI payloads

## Impact

- **New package**: `Agibuild.Fulora.AI` (net10.0) — core AI infrastructure, depends on `Microsoft.Extensions.AI.Abstractions`
- **Modified package**: `Agibuild.Fulora.Bridge.Generator` — new `AiToolSchemaEmitter` for `[AiTool]` attribute
- **Modified package**: `Agibuild.Fulora.Core` — `[AiTool]` attribute, `IAiContentFilter` interface, multimodal payload types
- **New npm package**: `@agibuild/bridge-ai` — JS client for AI services (chat, streaming, tool invocation)
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions` (MIT), `Polly` (BSD-3) for resilience patterns
- **Concrete AI providers** (Ollama, OpenAI, ONNX, etc.) are out of scope — shipped as independent NuGet packages later
