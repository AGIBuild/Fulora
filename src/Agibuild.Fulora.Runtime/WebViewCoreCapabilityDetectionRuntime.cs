using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// One-shot environment-option applier and factory for <see cref="ICookieManager"/> /
/// <see cref="ICommandManager"/> wrappers. All work happens during <see cref="WebViewCore"/>
/// construction; afterwards this runtime is a pure reference holder.
/// </summary>
/// <remarks>
/// <para>
/// Every capability this runtime touches — options, custom schemes, cookies, commands — is now a
/// mandatory facet of <see cref="IWebViewAdapter"/>, so no capability probing or null-propagation
/// remains here. The runtime holds the <see cref="WebViewCoreContext"/> directly and invokes the
/// inherited interface members on <see cref="WebViewCoreContext.Adapter"/>.
/// </para>
/// <para>
/// Intentionally not <see cref="IDisposable"/>: the instance holds only injected references which
/// are owned by <see cref="WebViewCore"/> and the caller — never allocates unmanaged handles,
/// timers, subscriptions, or background tasks. Safe to drop with the owning core at GC time.
/// </para>
/// </remarks>
internal sealed class WebViewCoreCapabilityDetectionRuntime
{
    private readonly WebViewCoreContext _context;

    public WebViewCoreCapabilityDetectionRuntime(WebViewCoreContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void ApplyEnvironmentOptions()
    {
        var options = _context.EnvironmentOptions;
        _context.Adapter.ApplyEnvironmentOptions(options);
        _context.Logger.LogDebug(
            "Environment options applied: DevTools={DevTools}, Ephemeral={Ephemeral}, UA={UA}",
            options.EnableDevTools,
            options.UseEphemeralSession,
            options.CustomUserAgent ?? "(default)");
    }

    public void RegisterConfiguredCustomSchemes()
    {
        var schemes = _context.EnvironmentOptions.CustomSchemes;
        if (schemes.Count == 0)
            return;

        _context.Adapter.RegisterCustomSchemes(schemes);
        _context.Logger.LogDebug("Custom schemes registered: {Count}", schemes.Count);
    }

    public ICookieManager CreateCookieManager()
    {
        var manager = new RuntimeCookieManager(_context.Adapter, _context);
        _context.Logger.LogDebug("Cookie support: enabled");
        return manager;
    }

    public ICommandManager CreateCommandManager()
    {
        var manager = new RuntimeCommandManager(_context.Adapter, _context);
        _context.Logger.LogDebug("Command support: enabled");
        return manager;
    }
}
