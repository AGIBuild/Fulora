using System.Runtime.CompilerServices;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiBridgeStreamingTests
{
    [Fact]
    public async Task StreamCompletion_yields_tokens_from_provider()
    {
        var tokens = new[] { "Hello", " ", "world", "!" };
        var client = new StreamingMockChatClient(tokens);

        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", client));
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var request = new AiChatRequest { Message = "test" };

        var received = new List<string>();
        await foreach (var token in bridge.StreamCompletion(request))
        {
            received.Add(token);
        }

        Assert.Equal(tokens, received);
    }

    [Fact]
    public async Task StreamCompletion_skips_empty_tokens()
    {
        var tokens = new[] { "a", "", "b", "" };
        var client = new StreamingMockChatClient(tokens);

        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", client));
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var received = new List<string>();
        await foreach (var token in bridge.StreamCompletion(new AiChatRequest { Message = "test" }))
        {
            received.Add(token);
        }

        Assert.Equal(new[] { "a", "b" }, received);
    }

    [Fact]
    public async Task StreamCompletion_with_no_provider_throws()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(_ => { });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in bridge.StreamCompletion(new AiChatRequest { Message = "test" }))
            {
            }
        });
    }

    [Fact]
    public async Task StreamCompletion_respects_cancellation()
    {
        var cts = new CancellationTokenSource();
        var client = new InfiniteStreamingMockChatClient();

        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", client));
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var received = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var token in bridge.StreamCompletion(new AiChatRequest { Message = "test" }, cts.Token))
            {
                received.Add(token);
                if (received.Count >= 3)
                    cts.Cancel();
            }
        });

        Assert.Equal(3, received.Count);
    }

    [Fact]
    public async Task StreamCompletion_uses_named_provider()
    {
        var fastClient = new StreamingMockChatClient(["fast-token"]);
        var smartClient = new StreamingMockChatClient(["smart-token"]);

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("fast", fastClient);
            ai.AddChatClient("smart", smartClient);
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var received = new List<string>();
        await foreach (var token in bridge.StreamCompletion(new AiChatRequest { Message = "test", Provider = "smart" }))
        {
            received.Add(token);
        }

        Assert.Equal(["smart-token"], received);
    }

    [Fact]
    public async Task StreamCompletion_passes_model_id_option()
    {
        var client = new CapturingStreamingMockChatClient(["token"]);

        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", client));
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        await foreach (var _ in bridge.StreamCompletion(new AiChatRequest { Message = "test", ModelId = "gpt-4o" }))
        {
        }

        Assert.Equal("gpt-4o", client.LastOptions?.ModelId);
    }

    private sealed class StreamingMockChatClient(string[] tokens) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(tokens))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var token in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, token);
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class InfiniteStreamingMockChatClient : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "infinite")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var i = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, $"token-{i++}");
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class CapturingStreamingMockChatClient(string[] tokens) : IChatClient
    {
        public ChatOptions? LastOptions { get; private set; }
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(tokens))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            foreach (var token in tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, token);
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
