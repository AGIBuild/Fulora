using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Delegating <see cref="IChatClient"/> that applies resilience policies (retry, timeout,
/// circuit breaker, rate limiting) around an inner client.
/// </summary>
public sealed class ResilientChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly ResiliencePipeline _pipeline;

    public ResilientChatClient(IChatClient inner, AiResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        _inner = inner;
        _pipeline = BuildPipeline(options);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _pipeline.ExecuteAsync(
            async ct => await _inner.GetResponseAsync(messages, options, ct),
            cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For streaming, resilience applies to the initial connection only.
        // Once streaming starts, we pass through chunks directly.
        var enumerable = await _pipeline.ExecuteAsync(
            ct =>
            {
                var stream = _inner.GetStreamingResponseAsync(messages, options, ct);
                return ValueTask.FromResult(stream);
            },
            cancellationToken);

        await foreach (var update in enumerable.WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ResilientChatClient))
            return this;
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => _inner.Dispose();

    private static ResiliencePipeline BuildPipeline(AiResilienceOptions opts)
    {
        var builder = new ResiliencePipelineBuilder();

        if (opts.RateLimitPermitCount > 0)
        {
            builder.AddRateLimiter(new System.Threading.RateLimiting.SlidingWindowRateLimiter(
                new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
                {
                    PermitLimit = opts.RateLimitPermitCount,
                    Window = opts.RateLimitWindow,
                    SegmentsPerWindow = 4,
                    AutoReplenishment = true,
                    QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
        }

        builder.AddTimeout(opts.Timeout);

        if (opts.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = opts.MaxRetries,
                Delay = opts.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>(ex => IsTransient(ex.StatusCode))
                    .Handle<TimeoutRejectedException>()
                    .Handle<TaskCanceledException>()
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 1.0,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = opts.CircuitBreakerThreshold,
            BreakDuration = opts.CircuitBreakerBreakDuration,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TimeoutRejectedException>()
        });

        return builder.Build();
    }

    private static bool IsTransient(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or null; // network-level error (no status code)
}
