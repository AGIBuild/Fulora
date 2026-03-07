using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class ContentGateChatClientTests
{
    [Fact]
    public async Task Passes_through_when_no_filters()
    {
        var inner = new EchoChatClient();
        var client = new ContentGateChatClient(inner, []);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Contains("echo: hello", response.Text);
    }

    [Fact]
    public async Task Blocks_input_throws_exception()
    {
        var inner = new EchoChatClient();
        var filter = new TestFilter(inputAction: ContentFilterAction.Block, inputReason: "toxic");
        var client = new ContentGateChatClient(inner, [filter]);

        var ex = await Assert.ThrowsAsync<AiContentBlockedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "bad words")]));
        Assert.Contains("toxic", ex.Reason);
    }

    [Fact]
    public async Task Transforms_input_before_sending()
    {
        var inner = new EchoChatClient();
        var filter = new TestFilter(
            inputAction: ContentFilterAction.Transform,
            inputTransform: "cleaned input");
        var client = new ContentGateChatClient(inner, [filter]);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "dirty input")]);

        Assert.Contains("echo: cleaned input", response.Text);
    }

    [Fact]
    public async Task Blocks_output_throws_exception()
    {
        var inner = new EchoChatClient();
        var filter = new TestFilter(outputAction: ContentFilterAction.Block, outputReason: "harmful output");
        var client = new ContentGateChatClient(inner, [filter]);

        var ex = await Assert.ThrowsAsync<AiContentBlockedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "trigger")]));
        Assert.Contains("harmful output", ex.Reason);
    }

    [Fact]
    public async Task Transforms_output_before_returning()
    {
        var inner = new EchoChatClient();
        var filter = new TestFilter(
            outputAction: ContentFilterAction.Transform,
            outputTransform: "safe response");
        var client = new ContentGateChatClient(inner, [filter]);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("safe response", response.Text);
    }

    [Fact]
    public async Task Streaming_omits_blocked_chunks()
    {
        var inner = new StreamingChatClient(["hello", "bad", "world"]);
        var filter = new TestFilter(
            outputBlockPattern: "bad");
        var client = new ContentGateChatClient(inner, [filter]);

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "test")]))
        {
            if (update.Text is not null)
                chunks.Add(update.Text);
        }

        Assert.Equal(["hello", "world"], chunks);
    }

    [Fact]
    public async Task System_messages_are_not_filtered()
    {
        var inner = new EchoChatClient();
        var filter = new TestFilter(inputAction: ContentFilterAction.Block, inputReason: "blocked");
        var client = new ContentGateChatClient(inner, [filter]);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.System, "system prompt")]);

        Assert.Contains("echo: system prompt", response.Text);
    }

    private sealed class EchoChatClient : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = string.Join(" ", messages.Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"echo: {text}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class StreamingChatClient(string[] chunks) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "not streaming")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in chunks)
            {
                yield return new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new TextContent(chunk)]
                };
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class TestFilter(
        ContentFilterAction inputAction = ContentFilterAction.Allow,
        string? inputReason = null,
        string? inputTransform = null,
        ContentFilterAction outputAction = ContentFilterAction.Allow,
        string? outputReason = null,
        string? outputTransform = null,
        string? outputBlockPattern = null) : IAiContentFilter
    {
        public Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken cancellationToken)
        {
            var result = inputAction switch
            {
                ContentFilterAction.Block => ContentFilterResult.Block(inputReason ?? "blocked"),
                ContentFilterAction.Transform => ContentFilterResult.Transform(inputTransform ?? content),
                _ => ContentFilterResult.Allow
            };
            return Task.FromResult(result);
        }

        public Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken cancellationToken)
        {
            if (outputBlockPattern is not null && content.Contains(outputBlockPattern))
                return Task.FromResult(ContentFilterResult.Block("pattern match"));

            var result = outputAction switch
            {
                ContentFilterAction.Block => ContentFilterResult.Block(outputReason ?? "blocked"),
                ContentFilterAction.Transform => ContentFilterResult.Transform(outputTransform ?? content),
                _ => ContentFilterResult.Allow
            };
            return Task.FromResult(result);
        }
    }
}
