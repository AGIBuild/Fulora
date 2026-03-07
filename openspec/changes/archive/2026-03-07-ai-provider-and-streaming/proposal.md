## Why

Phase 11/12 delivered `Agibuild.Fulora.AI` with core abstractions (provider registry, resilience, metering, content gate, structured output, tool schema). However the module is not yet **usable end-to-end**: there are no concrete provider packages, `IAiBridgeService` lacks streaming, and the AI Chat sample bypasses the Fulora AI module entirely. This change closes the gap so developers can `dotnet add package` a provider, wire DI, and get streaming AI chat in their hybrid app. Aligns with AI-Native Hybrid Runtime direction and **G1 (Type-Safe Bridge)** — streaming AI completion is a first-class bridge use case.

## What Changes

- Add `IAsyncEnumerable<string> StreamCompletion(AiChatRequest request, CancellationToken ct)` to `IAiBridgeService` + implementation
- Create `Agibuild.Fulora.AI.Ollama` NuGet package — `FuloraAiBuilder.AddOllama(name, endpoint?, model?)` extension
- Create `Agibuild.Fulora.AI.OpenAI` NuGet package — `FuloraAiBuilder.AddOpenAI(name, apiKey, model?)` extension
- Upgrade `avalonia-ai-chat` sample to use `AddFuloraAi()` + Ollama provider, removing hand-rolled `CreateChatClient()` logic
- Update `packages/bridge-ai` TS types to include `streamCompletion` async iterable

## Non-goals

- Building provider packages beyond Ollama and OpenAI (Anthropic, Azure OpenAI, etc. are future NuGet packages)
- Conversation history management (multi-turn memory belongs at application layer)
- Tool-calling / function-calling runtime in this change (tool schema already exists; orchestration is a separate change)

## Capabilities

### New Capabilities
- `ai-provider-ollama`: Ollama provider package with `FuloraAiBuilder` integration for local LLM zero-config experience
- `ai-provider-openai`: OpenAI provider package with `FuloraAiBuilder` integration for cloud AI coverage
- `ai-bridge-streaming`: Streaming chat completion on `IAiBridgeService` via `IAsyncEnumerable` bridge transport

### Modified Capabilities
- `ai-provider-integration`: Add streaming method to bridge service and update TS types
- `ai-streaming-sample`: Upgrade sample to use `Agibuild.Fulora.AI` module + provider packages

## Impact

- **New projects**: `src/Agibuild.Fulora.AI.Ollama/`, `src/Agibuild.Fulora.AI.OpenAI/`
- **Modified**: `IAiBridgeService`, `AiBridgeService`, `packages/bridge-ai/src/index.ts`, `samples/avalonia-ai-chat/`
- **Dependencies**: `Microsoft.Extensions.AI.Ollama`, `Microsoft.Extensions.AI.OpenAI` (as package refs in provider packages)
- **Tests**: New unit tests per provider package, integration tests for streaming bridge
