namespace Agibuild.Fulora;

/// <summary>
/// Built-in middleware that enforces sliding-window rate limiting.
/// </summary>
internal sealed class RateLimitMiddleware : IBridgeMiddleware
{
    private readonly RateLimit _limit;
    private readonly Queue<long> _timestamps = new();

    public RateLimitMiddleware(RateLimit limit)
    {
        _limit = limit;
    }

    public Task<object?> InvokeAsync(BridgeCallContext context, BridgeCallHandler pipeline)
    {
        var now = Environment.TickCount64;
        var windowMs = _limit.Window.Ticks / TimeSpan.TicksPerMillisecond;

        lock (_timestamps)
        {
            while (_timestamps.Count > 0 && now - _timestamps.Peek() > windowMs)
                _timestamps.Dequeue();

            if (_timestamps.Count >= _limit.MaxCalls)
                throw new WebViewRpcException(-32029, "Rate limit exceeded");

            _timestamps.Enqueue(now);
        }

        return pipeline(context);
    }
}
