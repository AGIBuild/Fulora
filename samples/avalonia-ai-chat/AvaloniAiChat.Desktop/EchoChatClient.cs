using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AvaloniAiChat.Desktop;

/// <summary>
/// Zero-dependency fallback that echoes the prompt back character-by-character,
/// simulating token streaming without any AI backend.
/// </summary>
public sealed class EchoChatClient : IChatClient
{
    public void Dispose() { }

    public ChatClientMetadata Metadata { get; } = new("echo");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.LastOrDefault()?.Text ?? "Hello!";
        var echo = $"[Echo] {lastMessage}";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, echo)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastMessage = chatMessages.LastOrDefault()?.Text ?? "Hello!";
        var echo = $"[Echo] {lastMessage}";

        foreach (var ch in echo)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, ch.ToString());
            await Task.Delay(30, cancellationToken);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
