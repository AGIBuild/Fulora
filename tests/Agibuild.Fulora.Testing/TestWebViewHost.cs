using Agibuild.Fulora;

namespace Agibuild.Fulora.Testing;

public sealed class TestWebViewHost : IWebView
{
    public Uri Source { get; set; } = new Uri("about:blank");
    public bool CanGoBack => false;
    public bool CanGoForward => false;
    public bool IsLoading => false;
    public Guid ChannelId { get; } = Guid.NewGuid();

    // Empty accessors — test stub never raises these interface-required events.
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted { add { } remove { } }
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted { add { } remove { } }
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested { add { } remove { } }
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived { add { } remove { } }
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested { add { } remove { } }
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested { add { } remove { } }
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested { add { } remove { } }
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested { add { } remove { } }
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated { add { } remove { } }
    public event EventHandler? AdapterDestroyed { add { } remove { } }

    public Task NavigateAsync(Uri uri) => Task.CompletedTask;
    public Task NavigateToStringAsync(string html) => Task.CompletedTask;
    public Task NavigateToStringAsync(string html, Uri? baseUrl) => Task.CompletedTask;
    public Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);

    public Task<bool> GoBackAsync() => Task.FromResult(false);
    public Task<bool> GoForwardAsync() => Task.FromResult(false);
    public Task<bool> RefreshAsync() => Task.FromResult(false);
    public Task<bool> StopAsync() => Task.FromResult(false);

    public ICookieManager? TryGetCookieManager() => null;
    public ICommandManager? TryGetCommandManager() => null;
    public Task<INativeHandle?> TryGetWebViewHandleAsync() => Task.FromResult<INativeHandle?>(null);
    public IWebViewRpcService? Rpc => null;
    public IBridgeTracer? BridgeTracer { get; set; }
    public IBridgeService Bridge => throw new NotSupportedException("TestWebViewHost does not support Bridge. Use WebViewCore with MockWebViewAdapter instead.");
    public Task OpenDevToolsAsync() => Task.CompletedTask;
    public Task CloseDevToolsAsync() => Task.CompletedTask;
    public Task<bool> IsDevToolsOpenAsync() => Task.FromResult(false);
    public Task<byte[]> CaptureScreenshotAsync() => Task.FromException<byte[]>(new NotSupportedException());
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => Task.FromException<byte[]>(new NotSupportedException());

    // Zoom
    public Task<double> GetZoomFactorAsync() => Task.FromResult(1.0);
    public Task SetZoomFactorAsync(double zoomFactor) => Task.CompletedTask;

    // Find in Page
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
        => Task.FromException<FindInPageEventArgs>(new NotSupportedException());
    public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;

    // Preload Scripts
    public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromException<string>(new NotSupportedException());
    public Task RemovePreloadScriptAsync(string scriptId) => Task.FromException(new NotSupportedException());

    // Context Menu
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested { add { } remove { } }

    public void Dispose() { /* No-op for test stub. */ }
}
