namespace Agibuild.Fulora.AI;

/// <summary>
/// Configuration options for AI call resilience (retry, timeout, circuit breaker, rate limit).
/// </summary>
public sealed class AiResilienceOptions
{
    /// <summary>Maximum number of retry attempts for transient failures. Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for exponential backoff. Default: 2 seconds.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Timeout for a single AI call. Default: 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of consecutive failures before circuit opens. Default: 5.</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>Duration the circuit stays open before allowing a trial call. Default: 60 seconds.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Maximum number of AI calls per time window. 0 = no limit. Default: 0.</summary>
    public int RateLimitPermitCount { get; set; }

    /// <summary>Time window for rate limiting. Default: 1 minute.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);
}
