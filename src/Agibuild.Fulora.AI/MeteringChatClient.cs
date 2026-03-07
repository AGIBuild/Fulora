using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Delegating <see cref="IChatClient"/> that extracts token usage from responses,
/// estimates costs, enforces budgets, and emits metrics.
/// </summary>
public sealed class MeteringChatClient : IChatClient
{
    private static readonly Meter s_meter = new("Agibuild.Fulora.AI", "1.0.0");
    private static readonly Counter<long> s_promptTokens = s_meter.CreateCounter<long>("fulora.ai.tokens.prompt");
    private static readonly Counter<long> s_completionTokens = s_meter.CreateCounter<long>("fulora.ai.tokens.completion");
    private static readonly Counter<double> s_estimatedCost = s_meter.CreateCounter<double>("fulora.ai.cost.estimated");

    private readonly IChatClient _inner;
    private readonly AiMeteringOptions _options;
    private readonly IBridgeTracer _tracer;
    private long _periodTokensUsed;
    private DateTime _periodStart;

    public MeteringChatClient(IChatClient inner, AiMeteringOptions options, IBridgeTracer? tracer = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        _inner = inner;
        _options = options;
        _tracer = tracer ?? NullBridgeTracer.Instance;
        _periodStart = DateTime.UtcNow;
    }

    /// <summary>Total tokens consumed in the current period.</summary>
    public long PeriodTokensUsed => _periodTokensUsed;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ResetPeriodIfNeeded();

        var response = await _inner.GetResponseAsync(messages, options, cancellationToken);
        ProcessUsage(response.Usage, options?.ModelId);
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ResetPeriodIfNeeded();

        await foreach (var update in _inner.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usageContent)
                    ProcessUsage(usageContent.Details, update.ModelId ?? options?.ModelId);
            }
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(MeteringChatClient))
            return this;
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => _inner.Dispose();

    private void ProcessUsage(UsageDetails? usage, string? modelId)
    {
        if (usage is null) return;

        var promptTokens = usage.InputTokenCount ?? 0;
        var completionTokens = usage.OutputTokenCount ?? 0;
        var totalTokens = promptTokens + completionTokens;

        if (totalTokens == 0) return;

        if (_options.SingleCallTokenLimit > 0 && totalTokens > _options.SingleCallTokenLimit)
        {
            throw new AiBudgetExceededException(
                $"AI call used {totalTokens} tokens, exceeding single-call limit of {_options.SingleCallTokenLimit}.");
        }

        Interlocked.Add(ref _periodTokensUsed, totalTokens);

        if (_options.PeriodBudgetTokens > 0 && _periodTokensUsed > _options.PeriodBudgetTokens)
        {
            throw new AiBudgetExceededException(
                $"AI token budget exceeded: {_periodTokensUsed}/{_options.PeriodBudgetTokens} tokens used in current period.");
        }

        s_promptTokens.Add(promptTokens);
        s_completionTokens.Add(completionTokens);

        var cost = EstimateCost(promptTokens, completionTokens, modelId);
        if (cost > 0)
            s_estimatedCost.Add((double)cost);

        _tracer.OnExportCallEnd("AI.Metering", modelId ?? "unknown",
            0, $"tokens={totalTokens},cost={cost:F6}");
    }

    internal decimal EstimateCost(long promptTokens, long completionTokens, string? modelId)
    {
        if (modelId is null || !_options.ModelPricing.TryGetValue(modelId, out var pricing))
            return 0m;

        return (promptTokens / 1000m) * pricing.PromptPer1K
             + (completionTokens / 1000m) * pricing.CompletionPer1K;
    }

    private void ResetPeriodIfNeeded()
    {
        if (_options.BudgetPeriod <= TimeSpan.Zero) return;
        if (DateTime.UtcNow - _periodStart < _options.BudgetPeriod) return;

        Interlocked.Exchange(ref _periodTokensUsed, 0);
        _periodStart = DateTime.UtcNow;
    }
}
