namespace Agibuild.Fulora;

internal sealed class WebViewControlRuntime
{
    private WebViewCore? _core;
    private bool _adapterUnavailable;
    private IBridgeTracer? _pendingBridgeTracer;

    public WebViewCore? Core => _core;

    public IWebViewRpcService? Rpc => _core?.Rpc;

    public IBridgeTracer? BridgeTracer
    {
        get => _core?.BridgeTracer ?? _pendingBridgeTracer;
        set
        {
            if (_core is not null)
                _core.BridgeTracer = value;
            else
                _pendingBridgeTracer = value;
        }
    }

    public IBridgeService Bridge => RequireCore().Bridge;

    public void AttachCore(WebViewCore core)
    {
        ArgumentNullException.ThrowIfNull(core);

        _core = core;
        _adapterUnavailable = false;

        if (_pendingBridgeTracer is not null)
        {
            _core.BridgeTracer = _pendingBridgeTracer;
            _pendingBridgeTracer = null;
        }
    }

    public void MarkAdapterUnavailable()
    {
        _core = null;
        _adapterUnavailable = true;
    }

    public void ClearCore()
    {
        _core = null;
    }

    public ICookieManager? TryGetCookieManager() => _core?.TryGetCookieManager();

    public ICommandManager? TryGetCommandManager() => _core?.TryGetCommandManager();

    public Task OpenDevToolsAsync() => RequireCore().OpenDevToolsAsync();

    public Task CloseDevToolsAsync() => RequireCore().CloseDevToolsAsync();

    public Task<bool> IsDevToolsOpenAsync() => RequireCore().IsDevToolsOpenAsync();

    public Task<byte[]> CaptureScreenshotAsync() => RequireCore().CaptureScreenshotAsync();

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => RequireCore().PrintToPdfAsync(options);

    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
        => RequireCore().FindInPageAsync(text, options);

    public Task SetZoomFactorAsync(double zoomFactor) => RequireCore().SetZoomFactorAsync(zoomFactor);

    public Task StopFindInPageAsync(bool clearHighlights = true) => RequireCore().StopFindInPageAsync(clearHighlights);

    public Task<string> AddPreloadScriptAsync(string javaScript) => RequireCore().AddPreloadScriptAsync(javaScript);

    public Task RemovePreloadScriptAsync(string scriptId) => RequireCore().RemovePreloadScriptAsync(scriptId);

    public INativeHandle? TryGetWebViewHandle() => _core?.TryGetWebViewHandle();

    public Task<INativeHandle?> TryGetWebViewHandleAsync()
        => _core is null ? Task.FromResult<INativeHandle?>(null) : _core.TryGetWebViewHandleAsync();

    public void SetCustomUserAgent(string? userAgent)
    {
        if (_core is null)
        {
            if (_adapterUnavailable)
                return;

            throw new InvalidOperationException(
                "WebView is not yet attached to the visual tree. Wait until the control is loaded before calling navigation methods.");
        }

        _core.SetCustomUserAgent(userAgent);
    }

    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireCore().EnableWebMessageBridge(options);
    }

    public void DisableWebMessageBridge() => RequireCore().DisableWebMessageBridge();

    public void EnableSpaHosting(SpaHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireCore().EnableSpaHosting(options);
    }

    public Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return RequireCore().InvokeScriptAsync(script);
    }

    public Task<bool> GoBackAsync() => _core is null ? Task.FromResult(false) : _core.GoBackAsync();

    public Task<bool> GoForwardAsync() => _core is null ? Task.FromResult(false) : _core.GoForwardAsync();

    public Task<bool> RefreshAsync() => _core is null ? Task.FromResult(false) : _core.RefreshAsync();

    public Task<bool> StopAsync() => _core is null ? Task.FromResult(false) : _core.StopAsync();

    public Task NavigateAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return RequireCore().NavigateAsync(uri);
    }

    public Task NavigateToStringAsync(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return RequireCore().NavigateToStringAsync(html);
    }

    public Task NavigateToStringAsync(string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        return RequireCore().NavigateToStringAsync(html, baseUrl);
    }

    private WebViewCore RequireCore()
    {
        if (_core is not null)
            return _core;

        if (_adapterUnavailable)
        {
            throw new PlatformNotSupportedException(
                "No WebView adapter is available for the current platform. WebView functionality is not supported.");
        }

        throw new InvalidOperationException(
            "WebView is not yet attached to the visual tree. Wait until the control is loaded before calling navigation methods.");
    }
}
