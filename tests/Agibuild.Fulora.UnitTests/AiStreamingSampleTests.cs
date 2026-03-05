using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Tests that verify the AI streaming integration patterns used by the ai-chat sample.
/// These exercise <see cref="IChatClient"/> streaming → <see cref="IAsyncEnumerable{T}"/> mapping,
/// cancellation propagation, and the echo-fallback client behavior.
/// </summary>
public sealed class AiStreamingSampleTests
{
    private sealed class MockStreamingChatClient : IChatClient
    {
        private readonly string[] _tokens;

        public MockStreamingChatClient(params string[] tokens)
        {
            _tokens = tokens;
        }

        public void Dispose() { }

        public ChatClientMetadata Metadata { get; } = new("mock");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = string.Join("", _tokens);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var token in _tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, token);
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class EchoChatClient : IChatClient
    {
        public void Dispose() { }

        public ChatClientMetadata Metadata { get; } = new("echo");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var lastMessage = chatMessages.LastOrDefault()?.Text ?? "Hello!";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"[Echo] {lastMessage}")));
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
                await Task.Delay(1, cancellationToken);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private static async IAsyncEnumerable<string> StreamCompletion(
        IChatClient chatClient,
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

    [Fact]
    public async Task StreamCompletion_with_mock_client_returns_all_tokens()
    {
        var client = new MockStreamingChatClient("Hello", " ", "World");
        var tokens = new List<string>();

        await foreach (var token in StreamCompletion(client, "test", TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        Assert.Equal(["Hello", " ", "World"], tokens);
    }

    [Fact]
    public async Task StreamCompletion_cancellation_stops_enumeration()
    {
        var client = new MockStreamingChatClient("a", "b", "c", "d", "e");
        using var cts = new CancellationTokenSource();
        var tokens = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var token in StreamCompletion(client, "test", cts.Token))
            {
                tokens.Add(token);
                if (tokens.Count == 2)
                    cts.Cancel();
            }
        });

        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public async Task EchoChatClient_streams_prompt_character_by_character()
    {
        var client = new EchoChatClient();
        var tokens = new List<string>();

        await foreach (var token in StreamCompletion(client, "Hi", TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        var expected = "[Echo] Hi";
        Assert.Equal(expected.Length, tokens.Count);
        Assert.Equal(expected, string.Join("", tokens));
    }

    [Fact]
    public async Task EchoChatClient_cancellation_stops_streaming()
    {
        var client = new EchoChatClient();
        using var cts = new CancellationTokenSource();
        var tokens = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var token in StreamCompletion(client, "Hello World", cts.Token))
            {
                tokens.Add(token);
                if (tokens.Count == 3)
                    cts.Cancel();
            }
        });

        Assert.Equal(3, tokens.Count);
    }

    [Fact]
    public async Task EchoChatClient_GetResponseAsync_returns_full_echo()
    {
        var client = new EchoChatClient();
        var messages = new[] { new ChatMessage(ChatRole.User, "Test prompt") };

        var response = await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);
        var lastMsg = response.Messages.Last();

        Assert.Equal("[Echo] Test prompt", lastMsg.Text);
    }

    [Fact]
    public void EchoChatClient_metadata_reports_echo_provider()
    {
        var client = new EchoChatClient();
        Assert.Equal("echo", client.Metadata.ProviderName);
    }
}
