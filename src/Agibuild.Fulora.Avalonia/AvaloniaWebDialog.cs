using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Agibuild.Fulora;

/// <summary>
/// Production implementation of <see cref="IWebDialog"/> backed by an Avalonia <see cref="Window"/>
/// containing a <see cref="WebView"/> control. The WebView control handles all native platform
/// adapter creation, NativeControlHost lifecycle, and attachment automatically.
/// <para>
/// Usage:
/// <code>
/// var dialog = new AvaloniaWebDialog();
/// dialog.Title = "Sign In";
/// dialog.Show();
/// await dialog.NavigateAsync(new Uri("https://example.com"));
/// </code>
/// </para>
/// </summary>
public sealed class AvaloniaWebDialog : IWebDialog, ISpaHostingWebView
{
    private readonly Window _window;
    private readonly WebView _webView;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    private bool _shown;

    // Internal test seam for validating option propagation without private reflection.
    internal WebView TestOnlyInnerWebView => _webView;

    /// <inheritdoc />
    public AvaloniaWebDialog(IWebViewEnvironmentOptions? options = null)
    {
        _webView = new WebView
        {
            EnvironmentOptions = options is null
                ? null
                : new WebViewEnvironmentOptions
                {
                    EnableDevTools = options.EnableDevTools,
                    UseEphemeralSession = options.UseEphemeralSession,
                    CustomUserAgent = options.CustomUserAgent,
                    CustomSchemes = [.. options.CustomSchemes],
                    PreloadScripts = [.. options.PreloadScripts]
                }
        };

        // Listen for the first NavigationCompleted to signal readiness.
        // This fires when the WebView's NativeControlHost has been created
        // and the adapter is attached and ready for navigation.
        _webView.NavigationStarted += OnFirstNavigationEvent;

        _window = new Window
        {
            Content = _webView,
            Width = 800,
            Height = 600,
            Title = "WebDialog",
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        _window.Closing += OnWindowClosing;

        // When the window is opened, the WebView attaches to the visual tree,
        // which triggers NativeControlHost.CreateNativeControlCore → adapter attach.
        // Mark ready once the layout pass completes.
        _window.Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _window.Opened -= OnWindowOpened;
        // After the window is opened, the visual tree is attached and
        // NativeControlHost.CreateNativeControlCore has been called.
        // Use a low-priority dispatch to allow the layout to finish.
        Dispatcher.UIThread.Post(() => _readyTcs.TrySetResult(), DispatcherPriority.Loaded);
    }

    private void OnFirstNavigationEvent(object? sender, NavigationStartingEventArgs e)
    {
        _webView.NavigationStarted -= OnFirstNavigationEvent;
        _readyTcs.TrySetResult();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_disposed)
        {
            Closing?.Invoke(this, EventArgs.Empty);
            // Mark disposed so subsequent API calls fail fast with ObjectDisposedException
            // instead of crashing inside the (now-destroyed) native WebView.
            _disposed = true;
            _readyTcs.TrySetResult(); // Unblock any pending EnsureReadyAsync waiters
        }
    }

    // ==== IWebDialog — Window Management ====
    // All window-management methods are thread-safe: if called from a non-UI thread
    // the operation is marshalled to the Avalonia UI thread automatically.

    /// <inheritdoc />
    public string? Title
    {
        get => _window.Title;
        set => RunOnUIThread(() => _window.Title = value);
    }

    /// <inheritdoc />
    public bool CanUserResize
    {
        get => _window.CanResize;
        set => RunOnUIThread(() => _window.CanResize = value);
    }

    /// <inheritdoc />
    public void Show()
    {
        _shown = true;
        RunOnUIThread(() => _window.Show());
    }

    /// <inheritdoc />
    public bool Show(INativeHandle owner)
    {
        _shown = true;
        RunOnUIThread(() => _window.Show());
        return true;
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_disposed) return;
        RunOnUIThread(() => _window.Close());
    }

    /// <inheritdoc />
    public bool Resize(int width, int height)
    {
        RunOnUIThread(() =>
        {
            _window.Width = width;
            _window.Height = height;
        });
        return true;
    }

    /// <inheritdoc />
    public bool Move(int x, int y)
    {
        RunOnUIThread(() => _window.Position = new PixelPoint(x, y));
        return true;
    }

    /// <inheritdoc />
    public event EventHandler? Closing;

    /// <inheritdoc />
    public void EnableSpaHosting(SpaHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RunOnUIThread(() => _webView.EnableSpaHosting(options));
    }

    // ==== IWebView — Delegated to embedded WebView control ====

    /// <inheritdoc />
    public Uri Source
    {
        get => _webView.Source ?? new Uri("about:blank");
        set => _webView.Source = value;
    }

    /// <inheritdoc />
    public bool CanGoBack => _webView.CanGoBack;
    /// <inheritdoc />
    public bool CanGoForward => _webView.CanGoForward;
    /// <inheritdoc />
    public bool IsLoading => _webView.IsLoading;
    /// <inheritdoc />
    public Guid ChannelId => _webView.ChannelId;

    /// <inheritdoc />
    public async Task NavigateAsync(Uri uri)
    {
        // Do NOT use ConfigureAwait(false) here: _webView is an Avalonia UI control
        // and must be accessed on the UI thread. Keeping the SynchronizationContext
        // ensures the continuation runs on the Avalonia dispatcher.
        await EnsureReadyAsync();
        ThrowIfDisposed();
        await _webView.NavigateAsync(uri);
    }

    /// <inheritdoc />
    public async Task NavigateToStringAsync(string html)
    {
        await EnsureReadyAsync();
        ThrowIfDisposed();
        await _webView.NavigateToStringAsync(html);
    }

    /// <inheritdoc />
    public async Task NavigateToStringAsync(string html, Uri? baseUrl)
    {
        await EnsureReadyAsync();
        ThrowIfDisposed();
        await _webView.NavigateToStringAsync(html, baseUrl);
    }

    /// <inheritdoc />
    public async Task<string?> InvokeScriptAsync(string script)
    {
        await EnsureReadyAsync();
        ThrowIfDisposed();
        return await _webView.InvokeScriptAsync(script);
    }

    /// <inheritdoc />
    public Task<bool> GoBackAsync() => _webView.GoBackAsync();
    /// <inheritdoc />
    public Task<bool> GoForwardAsync() => _webView.GoForwardAsync();
    /// <inheritdoc />
    public Task<bool> RefreshAsync() => _webView.RefreshAsync();
    /// <inheritdoc />
    public Task<bool> StopAsync() => _webView.StopAsync();

    /// <inheritdoc />
    public ICookieManager? TryGetCookieManager() => _webView.TryGetCookieManager();
    /// <inheritdoc />
    public ICommandManager? TryGetCommandManager() => _webView.TryGetCommandManager();
    /// <inheritdoc />
    public Task<INativeHandle?> TryGetWebViewHandleAsync() => _webView.TryGetWebViewHandleAsync();
    /// <inheritdoc />
    public IWebViewRpcService? Rpc => _webView.Rpc;
    /// <inheritdoc />
    public IBridgeTracer? BridgeTracer { get => _webView.BridgeTracer; set => _webView.BridgeTracer = value; }
    /// <inheritdoc />
    public IBridgeService Bridge => _webView.Bridge;
    /// <inheritdoc />
    public Task OpenDevToolsAsync() => _webView.OpenDevToolsAsync();
    /// <inheritdoc />
    public Task CloseDevToolsAsync() => _webView.CloseDevToolsAsync();
    /// <inheritdoc />
    public Task<bool> IsDevToolsOpenAsync() => _webView.IsDevToolsOpenAsync();
    /// <inheritdoc />
    public Task<byte[]> CaptureScreenshotAsync() => _webView.CaptureScreenshotAsync();
    /// <inheritdoc />
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => _webView.PrintToPdfAsync(options);

    /// <inheritdoc />
    public Task<double> GetZoomFactorAsync() => _webView.GetZoomFactorAsync();
    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor) => _webView.SetZoomFactorAsync(zoomFactor);

    /// <inheritdoc cref="WebViewCore.FindInPageAsync"/>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null) => _webView.FindInPageAsync(text, options);
    /// <inheritdoc cref="WebViewCore.StopFindInPageAsync"/>
    public Task StopFindInPageAsync(bool clearHighlights = true) => _webView.StopFindInPageAsync(clearHighlights);

    /// <inheritdoc cref="WebViewCore.AddPreloadScriptAsync"/>
    public Task<string> AddPreloadScriptAsync(string javaScript) => _webView.AddPreloadScriptAsync(javaScript);
    /// <inheritdoc cref="WebViewCore.RemovePreloadScriptAsync"/>
    public Task RemovePreloadScriptAsync(string scriptId) => _webView.RemovePreloadScriptAsync(scriptId);

    /// <inheritdoc cref="WebViewCore.ContextMenuRequested"/>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add => _webView.ContextMenuRequested += value;
        remove => _webView.ContextMenuRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted
    {
        add => _webView.NavigationStarted += value;
        remove => _webView.NavigationStarted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _webView.NavigationCompleted += value;
        remove => _webView.NavigationCompleted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _webView.NewWindowRequested += value;
        remove => _webView.NewWindowRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived
    {
        add => _webView.WebMessageReceived += value;
        remove => _webView.WebMessageReceived -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested
    {
        add => _webView.WebResourceRequested += value;
        remove => _webView.WebResourceRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add => _webView.EnvironmentRequested += value;
        remove => _webView.EnvironmentRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested
    {
        add => _webView.DownloadRequested += value;
        remove => _webView.DownloadRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested
    {
        add => _webView.PermissionRequested += value;
        remove => _webView.PermissionRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated
    {
        add => _webView.AdapterCreated += value;
        remove => _webView.AdapterCreated -= value;
    }

    /// <inheritdoc />
    public event EventHandler? AdapterDestroyed
    {
        add => _webView.AdapterDestroyed += value;
        remove => _webView.AdapterDestroyed -= value;
    }

    // ==== IDisposable ====

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _readyTcs.TrySetResult(); // unblock any waiters
        RunOnUIThread(() =>
        {
            _window.Closing -= OnWindowClosing;
            _window.Close();
        });
    }

    // ==== Private ====

    private Task EnsureReadyAsync()
    {
        ThrowIfDisposed();
        if (_readyTcs.Task.IsCompleted) return Task.CompletedTask;
        if (!_shown)
        {
            throw new InvalidOperationException(
                "WebDialog must be shown (Show()) before calling navigation methods. " +
                "The WebView needs to be attached to the visual tree first.");
        }
        return _readyTcs.Task;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Executes <paramref name="action"/> on the Avalonia UI thread.
    /// If already on the UI thread, runs synchronously; otherwise posts to the dispatcher.
    /// </summary>
    private static void RunOnUIThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
