using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// One-shot environment-option applier and factory for
/// <see cref="ICookieManager"/> / <see cref="ICommandManager"/> wrappers. All
/// work happens during <see cref="WebViewCore"/> construction; afterwards
/// this runtime is a pure reference holder.
/// </summary>
/// <remarks>
/// <para>
/// Every capability this runtime touches — options, custom schemes, cookies,
/// commands — is now a mandatory facet of <see cref="IWebViewAdapter"/>, so
/// no capability probing or null-propagation remains here. The runtime holds
/// the adapter directly and invokes the inherited interface members.
/// </para>
/// <para>
/// Intentionally not <see cref="IDisposable"/>: the instance holds only
/// injected references (the adapter, options, logger) which are owned by
/// <see cref="WebViewCore"/> and the caller — never allocates unmanaged
/// handles, timers, subscriptions, or background tasks. Safe to drop with
/// the owning <see cref="WebViewCore"/> at GC time.
/// </para>
/// </remarks>
internal sealed class WebViewCoreCapabilityDetectionRuntime
{
    private readonly IWebViewAdapter _adapter;
    private readonly IWebViewEnvironmentOptions _environmentOptions;
    private readonly ILogger _logger;

    public WebViewCoreCapabilityDetectionRuntime(
        IWebViewAdapter adapter,
        IWebViewEnvironmentOptions environmentOptions,
        ILogger logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _environmentOptions = environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ApplyEnvironmentOptions()
    {
        _adapter.ApplyEnvironmentOptions(_environmentOptions);
        _logger.LogDebug("Environment options applied: DevTools={DevTools}, Ephemeral={Ephemeral}, UA={UA}",
            _environmentOptions.EnableDevTools,
            _environmentOptions.UseEphemeralSession,
            _environmentOptions.CustomUserAgent ?? "(default)");
    }

    public void RegisterConfiguredCustomSchemes()
    {
        var schemes = _environmentOptions.CustomSchemes;
        if (schemes.Count == 0)
            return;

        _adapter.RegisterCustomSchemes(schemes);
        _logger.LogDebug("Custom schemes registered: {Count}", schemes.Count);
    }

    public ICookieManager CreateCookieManager(IWebViewCoreOperationHost host)
    {
        var manager = new RuntimeCookieManager(_adapter, host, _logger);
        _logger.LogDebug("Cookie support: enabled");
        return manager;
    }

    public ICommandManager CreateCommandManager(IWebViewCoreOperationHost host)
    {
        var manager = new RuntimeCommandManager(_adapter, host);
        _logger.LogDebug("Command support: enabled");
        return manager;
    }
}
