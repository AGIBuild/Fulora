using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal sealed class WebViewCoreSpaHostingRuntime : IDisposable
{
    private readonly WebViewCoreContext _context;
    private readonly WebViewCoreBridgeRuntime _bridgeRuntime;
    private SpaHostingService? _spaHostingService;

    public WebViewCoreSpaHostingRuntime(WebViewCoreContext context, WebViewCoreBridgeRuntime bridgeRuntime)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _bridgeRuntime = bridgeRuntime ?? throw new ArgumentNullException(nameof(bridgeRuntime));
    }

    public void EnableSpaHosting(SpaHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _context.ThrowIfDisposed();

        if (_spaHostingService is not null)
            throw new InvalidOperationException("SPA hosting is already enabled.");

        _spaHostingService = new SpaHostingService(options, _context.Logger);
        _context.Adapter.RegisterCustomSchemes([_spaHostingService.GetSchemeRegistration()]);
        _context.Events.WebResourceRequested += OnWebResourceRequested;

        if (options.AutoInjectBridgeScript && !_bridgeRuntime.IsBridgeEnabled)
            _bridgeRuntime.EnableWebMessageBridge(new WebMessageBridgeOptions());

        _context.Logger.LogDebug("SPA hosting enabled: scheme={Scheme}, devServer={DevServer}",
            options.Scheme, options.DevServerUrl ?? "(embedded)");
    }

    public void Dispose()
    {
        if (_spaHostingService is null)
            return;

        _context.Events.WebResourceRequested -= OnWebResourceRequested;
        _spaHostingService.Dispose();
        _spaHostingService = null;
    }

    internal void HandleWebResourceRequested(WebResourceRequestedEventArgs e)
        => _spaHostingService?.TryHandle(e);

    private void OnWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
        => HandleWebResourceRequested(e);
}
