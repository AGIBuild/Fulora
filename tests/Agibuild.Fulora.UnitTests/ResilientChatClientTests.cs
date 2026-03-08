using System.Net;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Polly.RateLimiting;
using Polly.Timeout;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class ResilientChatClientTests
{
    private static AiResilienceOptions DefaultOptions => new()
    {
        MaxRetries = 2,
        RetryBaseDelay = TimeSpan.FromMilliseconds(10),
        Timeout = TimeSpan.FromSeconds(5),
        CircuitBreakerThreshold = 10,
        CircuitBreakerBreakDuration = TimeSpan.FromSeconds(1)
    };

    [Fact]
    public async Task Retries_on_transient_then_succeeds()
    {
        var callCount = 0;
        var inner = new CallbackChatClient(async () =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("transient", null, HttpStatusCode.ServiceUnavailable);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        });

        var client = new ResilientChatClient(inner, DefaultOptions);
        var result = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, callCount);
        Assert.Contains("ok", result.Text);
    }

    [Fact]
    public async Task Does_not_retry_on_auth_error()
    {
        var callCount = 0;
        var inner = new CallbackChatClient(async () =>
        {
            callCount++;
            throw new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized);
        });

        var client = new ResilientChatClient(inner, DefaultOptions);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Throws_timeout_when_call_exceeds_limit()
    {
        var opts = DefaultOptions;
        opts.Timeout = TimeSpan.FromMilliseconds(50);
        opts.MaxRetries = 0;

        var inner = new CallbackChatClient(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "late"));
        });

        var client = new ResilientChatClient(inner, opts);

        await Assert.ThrowsAsync<TimeoutRejectedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rate_limiter_rejects_when_exceeded()
    {
        var opts = DefaultOptions;
        opts.RateLimitPermitCount = 1;
        opts.RateLimitWindow = TimeSpan.FromSeconds(10);
        opts.MaxRetries = 0;

        var inner = new CallbackChatClient(async () =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var client = new ResilientChatClient(inner, opts);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "1")], cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RateLimiterRejectedException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "2")], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetService_returns_self_for_own_type()
    {
        var inner = new CallbackChatClient(async () =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var client = new ResilientChatClient(inner, DefaultOptions);

        var service = client.GetService(typeof(ResilientChatClient));
        Assert.Same(client, service);
    }

    private sealed class CallbackChatClient : IChatClient
    {
        private readonly Func<CancellationToken, Task<ChatResponse>> _handler;

        public CallbackChatClient(Func<Task<ChatResponse>> handler)
        {
            _handler = _ => handler();
        }

        public CallbackChatClient(Func<CancellationToken, Task<ChatResponse>> handler)
        {
            _handler = handler;
        }

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => _handler(cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
