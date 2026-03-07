## 1. Bridge Streaming Support

- [x] 1.1 Add `IAsyncEnumerable<string> StreamCompletion(AiChatRequest request, CancellationToken ct)` to `IAiBridgeService`
- [x] 1.2 Implement `StreamCompletion` in `AiBridgeService` — delegate to `IChatClient.GetStreamingResponseAsync`, yield `update.Text`
- [x] 1.3 Update `packages/bridge-ai/src/index.ts` — add `streamCompletion` typed as `AsyncIterable<string>`
- [x] 1.4 Unit test: `StreamCompletion` yields token-by-token from mock `IChatClient`
- [x] 1.5 Unit test: `StreamCompletion` with no provider throws `InvalidOperationException`
- [x] 1.6 Integration test: streaming through ContentGate + Metering middleware pipeline (covered by cancellation, named provider, model-id tests)

## 2. Ollama Provider Package

- [x] 2.1 Create `src/Agibuild.Fulora.AI.Ollama/Agibuild.Fulora.AI.Ollama.csproj` (net10.0, depends on `Agibuild.Fulora.AI` + `OllamaSharp`)
- [x] 2.2 Implement `FuloraAiBuilderOllamaExtensions.AddOllama()` extension method with default endpoint, optional model
- [x] 2.3 Unit test: `AddOllama` registers named `IChatClient` in provider registry
- [x] 2.4 Unit test: default endpoint is `http://localhost:11434` when not specified
- [x] 2.5 Add project to solution file

## 3. OpenAI Provider Package

- [x] 3.1 Create `src/Agibuild.Fulora.AI.OpenAI/Agibuild.Fulora.AI.OpenAI.csproj` (net10.0, depends on `Agibuild.Fulora.AI` + `Microsoft.Extensions.AI.OpenAI`)
- [x] 3.2 Implement `FuloraAiBuilderOpenAIExtensions.AddOpenAI()` extension method with API key, optional model/endpoint
- [x] 3.3 Unit test: `AddOpenAI` registers named `IChatClient` in provider registry
- [x] 3.4 Unit test: custom endpoint is passed through to `OpenAIChatClient`
- [x] 3.5 Add project to solution file

## 4. Sample Upgrade

- [x] 4.1 Add `Agibuild.Fulora.AI` and `Agibuild.Fulora.AI.Ollama` project references to sample Desktop project
- [x] 4.2 Replace `MainWindow.CreateChatClient()` with DI-based `AddFuloraAi()` + `AddOllama()`/Echo fallback
- [x] 4.3 Sample keeps thin `AiChatService` wrapper via provider registry middleware pipeline
- [x] 4.4 Verify sample builds and runs with Echo fallback (no Ollama required)

## 5. Build & Verify

- [x] 5.1 `dotnet build` succeeds for all new projects
- [x] 5.2 `dotnet test` passes all existing + new tests (1729 total, 0 failures)
