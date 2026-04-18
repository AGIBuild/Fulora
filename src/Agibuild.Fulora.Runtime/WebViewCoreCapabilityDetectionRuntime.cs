using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// One-shot capability detector and factory for <see cref="ICookieManager"/> / <see cref="ICommandManager"/>
/// wrappers. All work happens during <see cref="WebViewCore"/> construction; afterwards this runtime is a
/// pure reference holder.
/// </summary>
/// <remarks>
/// Intentionally not <see cref="IDisposable"/>: the instance holds only injected references
/// (platform adapter capabilities, options, logger) which are owned by <see cref="WebViewCore"/>
/// and the caller — never allocates unmanaged handles, timers, subscriptions, or background
/// tasks. Safe to drop with the owning <see cref="WebViewCore"/> at GC time.
/// </remarks>
internal sealed class WebViewCoreCapabilityDetectionRuntime
{
    private readonly AdapterCapabilities _capabilities;
    private readonly IWebViewEnvironmentOptions _environmentOptions;
    private readonly ILogger _logger;

    public WebViewCoreCapabilityDetectionRuntime(
        AdapterCapabilities capabilities,
        IWebViewEnvironmentOptions environmentOptions,
        ILogger logger)
    {
        _capabilities = capabilities;
        _environmentOptions = environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ApplyEnvironmentOptions()
    {
        var adapterOptions = _capabilities.Options;
        if (adapterOptions is null)
            return;

        adapterOptions.ApplyEnvironmentOptions(_environmentOptions);
        _logger.LogDebug("Environment options applied: DevTools={DevTools}, Ephemeral={Ephemeral}, UA={UA}",
            _environmentOptions.EnableDevTools,
            _environmentOptions.UseEphemeralSession,
            _environmentOptions.CustomUserAgent ?? "(default)");
    }

    public void RegisterConfiguredCustomSchemes()
    {
        var customSchemeAdapter = _capabilities.CustomScheme;
        if (customSchemeAdapter is null)
            return;

        var schemes = _environmentOptions.CustomSchemes;
        if (schemes.Count == 0)
            return;

        customSchemeAdapter.RegisterCustomSchemes(schemes);
        _logger.LogDebug("Custom schemes registered: {Count}", schemes.Count);
    }

    public ICookieManager? CreateCookieManager(IWebViewCoreOperationHost host)
    {
        var cookieAdapter = _capabilities.Cookie;
        if (cookieAdapter is null)
            return null;

        var manager = new RuntimeCookieManager(cookieAdapter, host, _logger);
        _logger.LogDebug("Cookie support: enabled");
        return manager;
    }

    public ICommandManager? CreateCommandManager(IWebViewCoreOperationHost host)
    {
        var commandAdapter = _capabilities.Command;
        if (commandAdapter is null)
            return null;

        var manager = new RuntimeCommandManager(commandAdapter, host);
        _logger.LogDebug("Command support: enabled");
        return manager;
    }
}
