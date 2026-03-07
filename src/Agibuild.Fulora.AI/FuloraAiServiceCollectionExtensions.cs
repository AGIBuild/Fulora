using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        if (builder.ResilienceEnabled)
        {
            services.TryAddSingleton(builder.ResilienceOptions);
        }

        if (builder.MeteringEnabled)
        {
            services.TryAddSingleton(builder.MeteringOptions);
        }

        services.TryAddSingleton<IAiPayloadStore, InMemoryAiPayloadStore>();
        services.TryAddSingleton<IAiToolRegistry, AiToolRegistry>();
        services.TryAddTransient<IAiBridgeService, AiBridgeService>();

        return services;
    }
}
