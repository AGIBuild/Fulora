# AI Integration Guide

This guide explains how to build an AI-powered hybrid app with Fulora using `IAsyncEnumerable<T>` streaming and `Microsoft.Extensions.AI`.

## Architecture

```
┌──────────────────────────────────────────────────┐
│  React UI  (AvaloniAiChat.Web)                   │
│  ┌────────────────────────────────────────┐      │
│  │ for await (const token of             │      │
│  │   AiChatService.streamCompletion(p))  │      │
│  └──────────────┬─────────────────────────┘      │
└─────────────────┼────────────────────────────────┘
                  │ JSON-RPC + AsyncIterable bridge
┌─────────────────┼────────────────────────────────┐
│  Bridge Layer  (AvaloniAiChat.Bridge)            │
│  ┌──────────────┴─────────────────────────┐      │
│  │ [JsExport] IAiChatService              │      │
│  │   StreamCompletion(prompt, ct)         │      │
│  │   → IAsyncEnumerable<string>          │      │
│  └──────────────┬─────────────────────────┘      │
└─────────────────┼────────────────────────────────┘
                  │ C# implementation
┌─────────────────┼────────────────────────────────┐
│  Desktop Host  (AvaloniAiChat.Desktop)           │
│  ┌──────────────┴─────────────────────────┐      │
│  │ AiChatService : IAiChatService         │      │
│  │   wraps IChatClient from               │      │
│  │   Microsoft.Extensions.AI             │      │
│  └──────────────┬─────────────────────────┘      │
└─────────────────┼────────────────────────────────┘
                  │
          ┌───────┴───────┐
          │  IChatClient  │  Ollama / OpenAI / Echo
          └───────────────┘
```

## Streaming Patterns

### C# Side — `IAsyncEnumerable<T>`

Mark your bridge method with `IAsyncEnumerable<T>` return type. The source generator maps it to the JSON-RPC streaming protocol automatically:

```csharp
[JsExport]
public interface IAiChatService
{
    IAsyncEnumerable<string> StreamCompletion(
        string prompt,
        CancellationToken cancellationToken = default);
}
```

The implementation wraps any `IChatClient`:

```csharp
public sealed class AiChatService(IChatClient chatClient) : IAiChatService
{
    public async IAsyncEnumerable<string> StreamCompletion(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ChatMessage[] messages = [new(ChatRole.User, prompt)];
        await foreach (var update in chatClient
            .GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }
}
```

### JavaScript Side — `AsyncIterable`

The bridge maps `IAsyncEnumerable<T>` to a JS `AsyncIterable`. Use `for await...of`:

```typescript
const iterable = AiChatService.streamCompletion(prompt);
for await (const token of iterable) {
    appendToMessage(token);
}
```

## Cancellation

### From JavaScript

Use `AbortController` to cancel a streaming operation:

```typescript
const controller = new AbortController();
const iterable = AiChatService.streamCompletion(prompt, controller.signal);

// Later: cancel
controller.abort();
```

The bridge maps `AbortSignal` → `CancellationToken` on the C# side.

### Inactivity Timeout

Enumerators that are not polled for 30 seconds are automatically disposed by the runtime. This prevents resource leaks from abandoned streams.

## Backend Configuration

The sample selects an AI backend via environment variables:

| Variable | Value | Backend |
|----------|-------|---------|
| `AI__Provider` | `ollama` | Local Ollama instance |
| `AI__Provider` | `openai` | OpenAI API (requires `AI__ApiKey`) |
| *(not set)* | — | Echo mode (streams prompt back) |

### Ollama Setup

1. Install Ollama: https://ollama.com/download
2. Pull a model: `ollama pull llama3.2`
3. Run the sample:

```bash
export AI__Provider=ollama
export AI__Model=llama3.2
cd samples/avalonia-ai-chat
dotnet run --project AvaloniAiChat.Desktop
```

### Echo Mode (No Backend Required)

By default, the sample runs in echo mode — it streams the user's prompt back character by character. A banner in the UI indicates demo mode.
