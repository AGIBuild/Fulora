using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Extension methods for registering Fulora AI services.
/// </summary>
public static class FuloraAiServiceCollectionExtensions
{
    /// <summary>
    /// Adds Fulora AI infrastructure services (provider registry, middleware pipeline,
    /// payload store, tool registry, and bridge service).
    /// </summary>
    public static IServiceCollection AddFuloraAi(this IServiceCollection services, Action<FuloraAiBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new FuloraAiBuilder(services);
        configure(builder);

        var registry = builder.BuildRegistry();
        services.TryAddSingleton<IAiProviderRegistry>(registry);

        RegisterOptions(services, builder);

        services.TryAddSingleton<IAiPayloadStore, InMemoryAiPayloadStore>();
        services.TryAddSingleton<IAiToolRegistry, AiToolRegistry>();
        services.TryAddTransient<IAiBridgeService, AiBridgeService>();

        return services;
    }

    private static void RegisterOptions(IServiceCollection services, FuloraAiBuilder builder)
    {
        if (builder.ResilienceEnabled)
        {
            var src = builder.ResilienceOptions;
            services.AddOptions<AiResilienceOptions>()
                .Configure(o =>
                {
                    o.MaxRetries = src.MaxRetries;
                    o.RetryBaseDelay = src.RetryBaseDelay;
                    o.Timeout = src.Timeout;
                    o.CircuitBreakerThreshold = src.CircuitBreakerThreshold;
                    o.CircuitBreakerBreakDuration = src.CircuitBreakerBreakDuration;
                    o.RateLimitPermitCount = src.RateLimitPermitCount;
                    o.RateLimitWindow = src.RateLimitWindow;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        if (builder.MeteringEnabled)
        {
            var src = builder.MeteringOptions;
            services.AddOptions<AiMeteringOptions>()
                .Configure(o =>
                {
                    o.SingleCallTokenLimit = src.SingleCallTokenLimit;
                    o.PeriodBudgetTokens = src.PeriodBudgetTokens;
                    o.BudgetPeriod = src.BudgetPeriod;
                    foreach (var kvp in src.ModelPricing)
                        o.ModelPricing[kvp.Key] = kvp.Value;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }

        if (builder.ToolCallingEnabled)
        {
            var src = builder.ToolCallingOptions;
            services.AddOptions<AiToolCallingOptions>()
                .Configure(o => o.MaxIterations = src.MaxIterations)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
        else
        {
            services.AddOptions<AiToolCallingOptions>()
                .ValidateDataAnnotations();
        }

        if (builder.ConversationEnabled)
        {
            var src = builder.ConversationOptions;
            services.AddOptions<AiConversationOptions>()
                .Configure(o =>
                {
                    o.DefaultMaxTokens = src.DefaultMaxTokens;
                    o.SessionTtl = src.SessionTtl;
                    o.EstimateTokens = src.EstimateTokens;
                })
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
        else
        {
            services.AddOptions<AiConversationOptions>()
                .ValidateDataAnnotations();
        }

        services.TryAddSingleton<IAiConversationManager>(
            sp => new InMemoryAiConversationManager(sp.GetRequiredService<IOptions<AiConversationOptions>>()));
    }
}
