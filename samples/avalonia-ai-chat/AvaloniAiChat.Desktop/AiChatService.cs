using System.Runtime.CompilerServices;
using AvaloniAiChat.Bridge.Services;
using Microsoft.Extensions.AI;

namespace AvaloniAiChat.Desktop;

/// <summary>
/// AI chat service that wraps <see cref="IChatClient"/> to stream LLM responses
/// token-by-token via <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
public sealed class AiChatService(IChatClient chatClient, string backendName) : IAiChatService
{
    public async IAsyncEnumerable<string> StreamCompletion(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatMessage[] messages = [new(ChatRole.User, prompt)];

        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    public Task<string> GetBackendInfo() => Task.FromResult(backendName);
}
