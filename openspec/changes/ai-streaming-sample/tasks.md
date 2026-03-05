# AI Streaming Sample — Tasks

## 1. Enumerator Inactivity Timeout Fix

- [x] 1.1 Add per-enumerator `CancellationTokenSource` with 30s timeout in `WebViewRpcService.RegisterEnumerator`
- [x] 1.2 Reset timeout on each `$/enumerator/next/{token}` call
- [x] 1.3 On timeout: dispose enumerator, remove handler, return `{ values: [], finished: true }` for subsequent calls
- [x] 1.4 Add CT: enumerator disposed after 30s inactivity
- [x] 1.5 Add CT: polling resets the inactivity timer

## 2. Bridge Interface

- [x] 2.1 Create `samples/avalonia-ai-chat/AvaloniAiChat.Bridge/` project
- [x] 2.2 Define `IAiChatService` with `IAsyncEnumerable<string> StreamCompletion(string prompt, CancellationToken ct)`
- [x] 2.3 Define `ChatMessage` DTO (role, content, timestamp)
- [x] 2.4 Add `[JsExport]` attribute and configure source generator

## 3. C# Implementation

- [x] 3.1 Create `samples/avalonia-ai-chat/AvaloniAiChat.Desktop/` Avalonia host project
- [x] 3.2 Add `Microsoft.Extensions.AI` dependency
- [x] 3.3 Implement `AiChatService : IAiChatService` wrapping `IChatClient.CompleteStreamingAsync()`
- [x] 3.4 Implement `EchoChatClient : IChatClient` as zero-dependency fallback
- [x] 3.5 Configure DI: resolve `IChatClient` from env config (Ollama/OpenAI/Echo)
- [x] 3.6 Wire WebView, Bridge, and SPA hosting

## 4. React Chat UI

- [x] 4.1 Create `samples/avalonia-ai-chat/AvaloniAiChat.Web/` Vite + React project
- [x] 4.2 Install `@agibuild/bridge`
- [x] 4.3 Build chat UI: message list, input area, send button, stop button
- [x] 4.4 Implement streaming token rendering via `for await...of` on `AsyncIterable`
- [x] 4.5 Implement cancel via `AbortController`
- [x] 4.6 Show "demo mode" banner when using echo backend
- [x] 4.7 Auto-scroll and loading indicators

## 5. Documentation

- [x] 5.1 Create `docs/ai-integration-guide.md` covering: architecture, streaming patterns, cancellation, backend configuration
- [x] 5.2 Add sample entry to README samples table
- [x] 5.3 Document Ollama setup instructions for local development

## 6. Tests

- [x] 6.1 CT: `AiChatService` with mock `IChatClient` returns streaming tokens
- [x] 6.2 CT: `AiChatService` cancellation disposes client enumerator
- [x] 6.3 CT: `EchoChatClient` streams prompt back character by character
- [ ] 6.4 Manual IT: run sample app with Ollama and verify streaming
