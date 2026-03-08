using System.Text.Json;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class StructuredOutputTests
{
    [Fact]
    public async Task CompleteAsync_deserializes_valid_json()
    {
        var json = """{"name":"Alice","age":30}""";
        var inner = new SequentialChatClient([json]);
        var result = await inner.CompleteAsync<PersonDto>([new ChatMessage(ChatRole.User, "test")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
    }

    [Fact]
    public async Task CompleteAsync_retries_on_invalid_json_then_succeeds()
    {
        var responses = new[]
        {
            "not valid json",
            """{"name":"Bob","age":25}"""
        };
        var inner = new SequentialChatClient(responses);
        var result = await inner.CompleteAsync<PersonDto>(
            [new ChatMessage(ChatRole.User, "test")],
            maxRetries: 3,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public async Task CompleteAsync_throws_after_all_retries_exhausted()
    {
        var inner = new SequentialChatClient(["bad1", "bad2", "bad3", "bad4"]);

        var ex = await Assert.ThrowsAsync<AiStructuredOutputException>(
            () => inner.CompleteAsync<PersonDto>(
                [new ChatMessage(ChatRole.User, "test")],
                maxRetries: 3,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(ex.RawResponse);
        Assert.NotNull(ex.ValidationError);
        Assert.Contains(nameof(PersonDto), ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_handles_empty_response()
    {
        var inner = new SequentialChatClient(["", "", ""]);

        var ex = await Assert.ThrowsAsync<AiStructuredOutputException>(
            () => inner.CompleteAsync<PersonDto>(
                [new ChatMessage(ChatRole.User, "test")],
                maxRetries: 1,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("empty", ex.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_handles_null_deserialization()
    {
        var inner = new SequentialChatClient(["null", "null"]);

        var ex = await Assert.ThrowsAsync<AiStructuredOutputException>(
            () => inner.CompleteAsync<PersonDto>(
                [new ChatMessage(ChatRole.User, "test")],
                maxRetries: 0,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("null", ex.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChatResponseFormat_ForJsonSchema_returns_format()
    {
        var format = ChatResponseFormat.ForJsonSchema<PersonDto>();
        Assert.NotNull(format);
    }

    public sealed record PersonDto
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
    }

    private sealed class SequentialChatClient(string[] responses) : IChatClient
    {
        private int _callIndex;

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var idx = Math.Min(_callIndex, responses.Length - 1);
            _callIndex++;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, responses[idx])));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
