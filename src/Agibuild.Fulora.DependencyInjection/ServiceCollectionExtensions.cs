using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora.DependencyInjection;

/// <summary>Well-known HttpClient names for Fulora framework components.</summary>
public static class FuloraHttpClients
{
    /// <summary>Named HttpClient for <see cref="RemoteConfigProvider"/>.</summary>
    public const string RemoteConfig = "FuloraRemoteConfig";
}

/// <summary>
/// Dependency injection registrations for Agibuild WebView services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Agibuild WebView services in the DI container.
    /// <para>
    /// Registers:
    /// <list type="bullet">
    ///   <item><c>Func&lt;IWebViewDispatcher, IWebView&gt;</c> — factory for creating <see cref="IWebView"/> instances programmatically.</item>
    /// </list>
    /// </para>
    /// <para>
    /// After building the <see cref="IServiceProvider"/>, call
    /// <c>provider.UseAgibuildWebView()</c> (or
    /// <see cref="WebViewEnvironment.Initialize(ILoggerFactory?)"/> directly) so that XAML
    /// <c>&lt;agw:WebView /&gt;</c> controls automatically pick up <see cref="ILoggerFactory"/>
    /// and other shared services from DI.
    /// </para>
    /// </summary>
    public static IServiceCollection AddWebView(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IWebViewDispatcher>(_ => new SynchronizationContextWebViewDispatcher());

        services.AddSingleton<Func<IWebViewDispatcher, IWebView>>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<WebViewCore>()
                         ?? (ILogger<WebViewCore>)NullLogger<WebViewCore>.Instance;

            return dispatcher => WebViewCore.CreateDefault(dispatcher, logger);
        });

        return services;
    }

    /// <summary>
    /// Registers all Fulora framework services: WebView, telemetry, config, message bus,
    /// and auto-update. This is the recommended one-liner for full framework adoption.
    /// <para>
    /// <list type="bullet">
    ///   <item>WebView factory and dispatcher</item>
    ///   <item><see cref="ITelemetryProvider"/> (defaults to <see cref="NullTelemetryProvider"/>)</item>
    ///   <item><see cref="IWebViewMessageBus"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// For config and auto-update, use the chained methods:
    /// <c>services.AddFulora().AddJsonFileConfig(path).AddAutoUpdate(options)</c>.
    /// </para>
    /// </summary>
    public static FuloraServiceBuilder AddFulora(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWebView();
        services.AddSingleton<IWebViewMessageBus, WebViewMessageBus>();

        if (!services.Any(d => d.ServiceType == typeof(ITelemetryProvider)))
        {
            services.AddSingleton<ITelemetryProvider>(NullTelemetryProvider.Instance);
        }

        return new FuloraServiceBuilder(services);
    }
}

/// <summary>
/// Fluent builder returned by <see cref="ServiceCollectionExtensions.AddFulora"/> for chaining
/// optional service registrations.
/// </summary>
public sealed class FuloraServiceBuilder
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }

    internal FuloraServiceBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    /// Registers a <see cref="JsonFileConfigProvider"/> as the <see cref="IConfigProvider"/>.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    public FuloraServiceBuilder AddJsonFileConfig(string filePath)
    {
        Services.AddSingleton<IConfigProvider>(new JsonFileConfigProvider(filePath));
        return this;
    }

    /// <summary>
    /// Registers a <see cref="RemoteConfigProvider"/> as the <see cref="IConfigProvider"/>,
    /// optionally falling back to a local JSON file.
    /// </summary>
    /// <param name="remoteUri">The remote configuration endpoint URI.</param>
    /// <param name="localFallbackPath">Optional local JSON fallback file path.</param>
    public FuloraServiceBuilder AddRemoteConfig(Uri remoteUri, string? localFallbackPath = null)
    {
        Services.AddHttpClient(FuloraHttpClients.RemoteConfig);

        Services.AddSingleton<IConfigProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = factory.CreateClient(FuloraHttpClients.RemoteConfig);
            IConfigProvider? fallback = localFallbackPath != null
                ? new JsonFileConfigProvider(localFallbackPath)
                : null;
            return new RemoteConfigProvider(httpClient, remoteUri, fallback);
        });
        return this;
    }

    /// <summary>
    /// Registers a custom <see cref="ITelemetryProvider"/> (replaces the default no-op provider).
    /// </summary>
    public FuloraServiceBuilder AddTelemetry(ITelemetryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var existing = Services.FirstOrDefault(d => d.ServiceType == typeof(ITelemetryProvider));
        if (existing != null) Services.Remove(existing);
        Services.AddSingleton(provider);
        return this;
    }

    /// <summary>
    /// Registers the <see cref="AutoUpdateService"/> with the given options and platform provider.
    /// </summary>
    public FuloraServiceBuilder AddAutoUpdate(AutoUpdateOptions options, IAutoUpdatePlatformProvider platformProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platformProvider);
        Services.AddSingleton<IAutoUpdateService>(new AutoUpdateService(platformProvider, options));
        return this;
    }

    /// <summary>
    /// Registers the <see cref="ISharedStateStore"/> as a singleton.
    /// </summary>
    public FuloraServiceBuilder AddSharedState()
    {
        Services.AddSingleton<ISharedStateStore, SharedStateStore>();
        return this;
    }

    /// <summary>
    /// Registers a bridge configuration action that will be invoked by
    /// <see cref="WebViewBootstrapExtensions.BootstrapSpaAsync"/> when no explicit
    /// <see cref="SpaBootstrapOptions.ConfigureBridge"/> is provided.
    /// Use this to declare plugin-first bridge exposure at DI registration time.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddFulora()
    ///     .ConfigureBridge((bridge, sp) => bridge.UsePlugin&lt;MyPlugin&gt;(sp));
    /// </code>
    /// </example>
    public FuloraServiceBuilder ConfigureBridge(Action<IBridgeService, IServiceProvider?> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.AddSingleton(new BridgeConfigurationAction(configure));
        return this;
    }
}
