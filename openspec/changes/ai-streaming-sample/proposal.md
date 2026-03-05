## Why

AI-powered applications are the dominant developer demand in 2026. Fulora's `IAsyncEnumerable<T>` bridge streaming (Phase 8) provides the exact infrastructure needed for token-by-token LLM streaming from C# to JS, but no sample, documentation, or best-practice guidance exists. Developers evaluating hybrid frameworks will immediately ask "can I build a ChatGPT-like app with this?" — we need a compelling answer. Goal: G1 (Type-Safe Bridge), E1 (Project Template), Phase 12 — Developer Attraction.

## What Changes

- New sample app `samples/avalonia-ai-chat` — Avalonia + React chat UI with streaming LLM responses
- New `[JsExport]` service `IAiChatService` demonstrating `IAsyncEnumerable<string>` for token streaming
- C# backend using `Microsoft.Extensions.AI` abstraction (supports OpenAI, Azure OpenAI, Ollama, etc.)
- React chat UI with real-time token rendering via `for await...of` on bridge `AsyncIterable`
- Documentation: "Building AI Apps with Fulora" guide in `docs/`
- Bridge event pattern for "user is typing" (JS → C# notification)

## Capabilities

### New Capabilities

- `ai-streaming-sample`: Reference sample for AI/LLM integration with bridge streaming

### Modified Capabilities

- `bridge-async-enumerable`: Add inactivity timeout (30s) per spec (currently unimplemented)

## Non-goals

- Building a general-purpose AI SDK or plugin
- Bundling any LLM model or API key
- Production-ready AI service (this is a reference pattern)
- Prompt engineering or RAG patterns

## Impact

- New: `samples/avalonia-ai-chat/` (Avalonia host + React frontend + Bridge interfaces)
- New: `docs/ai-integration-guide.md`
- Modified: `WebViewRpcService.cs` (add enumerator inactivity timeout)
- New tests for inactivity timeout behavior
