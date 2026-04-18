using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal interface IWebViewCoreSpaHostingHost : IWebViewCoreDisposalHost
{
    bool IsBridgeEnabled { get; }
    void EnableWebMessageBridge(WebMessageBridgeOptions options);
    void RegisterCustomScheme(CustomSchemeRegistration registration);
    void AddWebResourceRequestedHandler(EventHandler<WebResourceRequestedEventArgs> handler);
    void RemoveWebResourceRequestedHandler(EventHandler<WebResourceRequestedEventArgs> handler);
}

internal sealed class WebViewCoreSpaHostingRuntime : IDisposable
{
    private readonly IWebViewCoreSpaHostingHost _host;
    private readonly ILogger _logger;
    private SpaHostingService? _spaHostingService;

    public WebViewCoreSpaHostingRuntime(IWebViewCoreSpaHostingHost host, ILogger logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void EnableSpaHosting(SpaHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _host.ThrowIfDisposed();

        if (_spaHostingService is not null)
            throw new InvalidOperationException("SPA hosting is already enabled.");

        _spaHostingService = new SpaHostingService(options, _logger);
        _host.RegisterCustomScheme(_spaHostingService.GetSchemeRegistration());
        _host.AddWebResourceRequestedHandler(OnWebResourceRequested);

        if (options.AutoInjectBridgeScript && !_host.IsBridgeEnabled)
            _host.EnableWebMessageBridge(new WebMessageBridgeOptions());

        _logger.LogDebug("SPA hosting enabled: scheme={Scheme}, devServer={DevServer}",
            options.Scheme, options.DevServerUrl ?? "(embedded)");
    }

    public void Dispose()
    {
        if (_spaHostingService is null)
            return;

        _host.RemoveWebResourceRequestedHandler(OnWebResourceRequested);
        _spaHostingService.Dispose();
        _spaHostingService = null;
    }

    internal void HandleWebResourceRequested(WebResourceRequestedEventArgs e)
        => _spaHostingService?.TryHandle(e);

    private void OnWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
        => HandleWebResourceRequested(e);
}
