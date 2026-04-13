namespace Agibuild.Fulora;

internal interface IWebViewCoreFeatureOperations
{
    Task OpenDevToolsAsync();
    Task CloseDevToolsAsync();
    Task<bool> IsDevToolsOpenAsync();
    Task<byte[]> CaptureScreenshotAsync();
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null);
    Task<double> GetZoomFactorAsync();
    Task SetZoomFactorAsync(double zoomFactor);
    Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null);
    Task StopFindInPageAsync(bool clearHighlights = true);
    Task<string> AddPreloadScriptAsync(string javaScript);
    Task RemovePreloadScriptAsync(string scriptId);
    Task<INativeHandle?> TryGetWebViewHandleAsync();
    void SetCustomUserAgent(string? userAgent);
}

internal interface IWebViewCoreBridgeOperations
{
    IWebViewRpcService? Rpc { get; }
    IBridgeTracer? BridgeTracer { get; set; }
    IBridgeService Bridge { get; }
    void EnableWebMessageBridge(WebMessageBridgeOptions options);
    void DisableWebMessageBridge();
    void ReinjectBridgeStubsIfEnabled();
}

internal sealed class WebViewCoreCapabilityRuntime
{
    private readonly IWebViewCoreFeatureOperations _feature;
    private readonly IWebViewCoreBridgeOperations _bridge;
    private readonly ICookieManager? _cookieManager;
    private readonly ICommandManager? _commandManager;

    public WebViewCoreCapabilityRuntime(
        IWebViewCoreFeatureOperations feature,
        IWebViewCoreBridgeOperations bridge,
        ICookieManager? cookieManager,
        ICommandManager? commandManager)
    {
        _feature = feature ?? throw new ArgumentNullException(nameof(feature));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cookieManager = cookieManager;
        _commandManager = commandManager;
    }

    public ICookieManager? TryGetCookieManager() => _cookieManager;

    public ICommandManager? TryGetCommandManager() => _commandManager;

    public IWebViewRpcService? Rpc => _bridge.Rpc;

    public IBridgeTracer? BridgeTracer
    {
        get => _bridge.BridgeTracer;
        set => _bridge.BridgeTracer = value;
    }

    public IBridgeService Bridge => _bridge.Bridge;

    public Task OpenDevToolsAsync() => _feature.OpenDevToolsAsync();
    public Task CloseDevToolsAsync() => _feature.CloseDevToolsAsync();
    public Task<bool> IsDevToolsOpenAsync() => _feature.IsDevToolsOpenAsync();
    public Task<byte[]> CaptureScreenshotAsync() => _feature.CaptureScreenshotAsync();
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => _feature.PrintToPdfAsync(options);
    public Task<double> GetZoomFactorAsync() => _feature.GetZoomFactorAsync();
    public Task SetZoomFactorAsync(double zoomFactor) => _feature.SetZoomFactorAsync(zoomFactor);
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null) => _feature.FindInPageAsync(text, options);
    public Task StopFindInPageAsync(bool clearHighlights = true) => _feature.StopFindInPageAsync(clearHighlights);
    public Task<string> AddPreloadScriptAsync(string javaScript) => _feature.AddPreloadScriptAsync(javaScript);
    public Task RemovePreloadScriptAsync(string scriptId) => _feature.RemovePreloadScriptAsync(scriptId);
    public Task<INativeHandle?> TryGetWebViewHandleAsync() => _feature.TryGetWebViewHandleAsync();
    public void SetCustomUserAgent(string? userAgent) => _feature.SetCustomUserAgent(userAgent);
    public void EnableWebMessageBridge(WebMessageBridgeOptions options) => _bridge.EnableWebMessageBridge(options);
    public void DisableWebMessageBridge() => _bridge.DisableWebMessageBridge();
    public void ReinjectBridgeStubsIfEnabled() => _bridge.ReinjectBridgeStubsIfEnabled();
}
