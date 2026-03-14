using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Runtime implementation of <see cref="IWebDialog"/>.
/// Wraps a <see cref="WebViewCore"/> and delegates window management to a <see cref="IDialogHost"/>.
/// </summary>
public sealed class WebDialog : IWebDialog, ISpaHostingWebView
{
    private readonly IDialogHost _host;
    private readonly WebViewCore _core;
    private bool _disposed;

    /// <summary>
    /// Creates a WebDialog with the given dialog host and adapter.
    /// The dialog host provides window management (Show/Close/Resize/Move).
    /// </summary>
    internal WebDialog(IDialogHost host, IWebViewAdapter adapter, IWebViewDispatcher dispatcher, ILogger<WebViewCore>? logger = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _core = new WebViewCore(adapter, dispatcher, logger ?? NullLogger<WebViewCore>.Instance);

        _host.HostClosing += OnHostClosing;
    }

    // ==== IWebDialog ====

    /// <inheritdoc />
    public string? Title
    {
        get => _host.Title;
        set => _host.Title = value;
    }

    /// <inheritdoc />
    public bool CanUserResize
    {
        get => _host.CanUserResize;
        set => _host.CanUserResize = value;
    }

    /// <inheritdoc />
    public void Show() => _host.Show();

    /// <inheritdoc />
    public bool Show(INativeHandle owner) => _host.ShowWithOwner(owner);

    /// <inheritdoc />
    public void Close()
    {
        _host.Close();
    }

    /// <inheritdoc />
    public bool Resize(int width, int height) => _host.Resize(width, height);

    /// <inheritdoc />
    public bool Move(int x, int y) => _host.Move(x, y);

    /// <inheritdoc />
    public event EventHandler? Closing;

    /// <inheritdoc />
    public void EnableSpaHosting(SpaHostingOptions options) => _core.EnableSpaHosting(options);

    // ==== IWebView delegation ====

    /// <inheritdoc />
    public Uri Source
    {
        get => _core.Source;
        set => _core.Source = value;
    }

    /// <inheritdoc />
    public bool CanGoBack => _core.CanGoBack;
    /// <inheritdoc />
    public bool CanGoForward => _core.CanGoForward;
    /// <inheritdoc />
    public bool IsLoading => _core.IsLoading;
    /// <inheritdoc />
    public Guid ChannelId => _core.ChannelId;

    /// <inheritdoc />
    public Task NavigateAsync(Uri uri) => _core.NavigateAsync(uri);
    /// <inheritdoc />
    public Task NavigateToStringAsync(string html) => _core.NavigateToStringAsync(html);
    /// <inheritdoc />
    public Task NavigateToStringAsync(string html, Uri? baseUrl) => _core.NavigateToStringAsync(html, baseUrl);
    /// <inheritdoc />
    public Task<string?> InvokeScriptAsync(string script) => _core.InvokeScriptAsync(script);

    /// <inheritdoc />
    public Task<bool> GoBackAsync() => _core.GoBackAsync();
    /// <inheritdoc />
    public Task<bool> GoForwardAsync() => _core.GoForwardAsync();
    /// <inheritdoc />
    public Task<bool> RefreshAsync() => _core.RefreshAsync();
    /// <inheritdoc />
    public Task<bool> StopAsync() => _core.StopAsync();

    /// <inheritdoc />
    public ICookieManager? TryGetCookieManager() => _core.TryGetCookieManager();
    /// <inheritdoc />
    public ICommandManager? TryGetCommandManager() => _core.TryGetCommandManager();
    /// <inheritdoc />
    public Task<INativeHandle?> TryGetWebViewHandleAsync() => _core.TryGetWebViewHandleAsync();
    /// <inheritdoc />
    public IWebViewRpcService? Rpc => _core.Rpc;
    /// <inheritdoc />
    public IBridgeTracer? BridgeTracer { get => _core.BridgeTracer; set => _core.BridgeTracer = value; }
    /// <inheritdoc />
    public IBridgeService Bridge => _core.Bridge;
    /// <inheritdoc />
    public Task OpenDevToolsAsync() => _core.OpenDevToolsAsync();
    /// <inheritdoc />
    public Task CloseDevToolsAsync() => _core.CloseDevToolsAsync();
    /// <inheritdoc />
    public Task<bool> IsDevToolsOpenAsync() => _core.IsDevToolsOpenAsync();
    /// <inheritdoc />
    public Task<byte[]> CaptureScreenshotAsync() => _core.CaptureScreenshotAsync();
    /// <inheritdoc />
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => _core.PrintToPdfAsync(options);

    /// <inheritdoc />
    public Task<double> GetZoomFactorAsync() => _core.GetZoomFactorAsync();
    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor) => _core.SetZoomFactorAsync(zoomFactor);

    /// <inheritdoc cref="WebViewCore.FindInPageAsync"/>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null) => _core.FindInPageAsync(text, options);
    /// <inheritdoc cref="WebViewCore.StopFindInPageAsync"/>
    public Task StopFindInPageAsync(bool clearHighlights = true) => _core.StopFindInPageAsync(clearHighlights);

    /// <inheritdoc cref="WebViewCore.AddPreloadScriptAsync"/>
    public Task<string> AddPreloadScriptAsync(string javaScript) => _core.AddPreloadScriptAsync(javaScript);
    /// <inheritdoc cref="WebViewCore.RemovePreloadScriptAsync"/>
    public Task RemovePreloadScriptAsync(string scriptId) => _core.RemovePreloadScriptAsync(scriptId);

    /// <inheritdoc cref="WebViewCore.ContextMenuRequested"/>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add => _core.ContextMenuRequested += value;
        remove => _core.ContextMenuRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted
    {
        add => _core.NavigationStarted += value;
        remove => _core.NavigationStarted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _core.NavigationCompleted += value;
        remove => _core.NavigationCompleted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _core.NewWindowRequested += value;
        remove => _core.NewWindowRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived
    {
        add => _core.WebMessageReceived += value;
        remove => _core.WebMessageReceived -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested
    {
        add => _core.WebResourceRequested += value;
        remove => _core.WebResourceRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add => _core.EnvironmentRequested += value;
        remove => _core.EnvironmentRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested
    {
        add => _core.DownloadRequested += value;
        remove => _core.DownloadRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested
    {
        add => _core.PermissionRequested += value;
        remove => _core.PermissionRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated
    {
        add => _core.AdapterCreated += value;
        remove => _core.AdapterCreated -= value;
    }

    /// <inheritdoc />
    public event EventHandler? AdapterDestroyed
    {
        add => _core.AdapterDestroyed += value;
        remove => _core.AdapterDestroyed -= value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _host.HostClosing -= OnHostClosing;
        _core.Dispose();
        _host.Close();
    }

    private void OnHostClosing(object? sender, EventArgs e)
    {
        Closing?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Abstraction for the dialog window host (platform window management).
/// Kept host-framework-neutral to enable unit testing with mocks.
/// </summary>
public interface IDialogHost
{
    /// <summary>Window title.</summary>
    string? Title { get; set; }
    /// <summary>Whether user can resize the window.</summary>
    bool CanUserResize { get; set; }

    /// <summary>Shows the host window.</summary>
    void Show();
    /// <summary>Shows the host window with an owner handle.</summary>
    bool ShowWithOwner(INativeHandle owner);
    /// <summary>Closes the host window.</summary>
    void Close();
    /// <summary>Resizes the host window.</summary>
    bool Resize(int width, int height);
    /// <summary>Moves the host window.</summary>
    bool Move(int x, int y);

    /// <summary>Raised when host window is closing.</summary>
    event EventHandler? HostClosing;
}
