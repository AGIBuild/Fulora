using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Testing;

internal class MockWebViewAdapter : IWebViewAdapter
{
    private IWebViewAdapterHost? _host;
    private bool _initialized;
    private bool _attached;
    private bool _detached;

    // In-memory cookie store keyed by "name|domain|path" (used by MockWebViewAdapterWithCookies)
    protected readonly Dictionary<string, WebViewCookie> CookieStore = new();

    /// <summary>Creates a mock without cookie support. Use <see cref="CreateWithCookies"/> for cookie-enabled mock.</summary>
    public static MockWebViewAdapter Create() => new();

    /// <summary>Creates a mock that also implements <see cref="ICookieAdapter"/>.</summary>
    public static MockWebViewAdapterWithCookies CreateWithCookies() => new();

    public Guid? LastNavigationId { get; private set; }
    public Uri? LastNavigationUri { get; private set; }
    public Uri? LastBaseUrl { get; private set; }
    public int? LastNavigateThreadId { get; private set; }
    public int? LastNavigateToStringThreadId { get; private set; }
    public int? LastInvokeScriptThreadId { get; private set; }
    public int NavigateCallCount { get; private set; }
    public string? ScriptResult { get; set; }
    public Exception? ScriptException { get; set; }

    /// <summary>Optional callback invoked on every InvokeScriptAsync call. Return value overrides ScriptResult.</summary>
    public Func<string, string?>? ScriptCallback { get; set; }

    /// <summary>The last script passed to InvokeScriptAsync.</summary>
    public string? LastScript { get; private set; }

    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;

    public void Initialize(IWebViewAdapterHost host)
    {
        if (_initialized)
        {
            throw new InvalidOperationException("Initialize can only be called once.");
        }

        _host = host ?? throw new ArgumentNullException(nameof(host));
        _initialized = true;
    }

    public void Attach(INativeHandle parentHandle)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Attach requires Initialize first.");
        }

        if (_attached)
        {
            throw new InvalidOperationException("Attach can only be called once.");
        }

        _attached = true;
        AttachCallCount++;
    }

    public void Detach()
    {
        if (!_attached)
        {
            throw new InvalidOperationException("Detach requires Attach first.");
        }

        if (_detached)
        {
            throw new InvalidOperationException("Detach can only be called once.");
        }

        _detached = true;
        DetachCallCount++;
    }

    /// <summary>
    /// When true, NavigateAsync automatically raises NavigationCompleted(Success)
    /// and then calls <see cref="OnNavigationAutoCompleted"/> if set.
    /// Useful for integration-style tests like WebAuthBroker flows.
    /// </summary>
    public bool AutoCompleteNavigation { get; set; }

    /// <summary>Callback invoked after auto-completing navigation. Use to simulate post-navigation events.</summary>
    public Action? OnNavigationAutoCompleted { get; set; }

    public Task NavigateAsync(Guid navigationId, Uri uri)
    {
        NavigateCallCount++;
        LastNavigateThreadId = Environment.CurrentManagedThreadId;
        LastNavigationId = navigationId;
        LastNavigationUri = uri;

        if (AutoCompleteNavigation)
        {
            RaiseNavigationCompleted(NavigationCompletedStatus.Success);
            OnNavigationAutoCompleted?.Invoke();
        }

        return Task.CompletedTask;
    }

    public Task NavigateToStringAsync(Guid navigationId, string html)
        => NavigateToStringAsync(navigationId, html, baseUrl: null);

    public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
    {
        LastNavigateToStringThreadId = Environment.CurrentManagedThreadId;
        LastNavigationId = navigationId;
        LastNavigationUri = null;
        LastBaseUrl = baseUrl;

        if (AutoCompleteNavigation)
        {
            RaiseNavigationCompleted(NavigationCompletedStatus.Success);
            OnNavigationAutoCompleted?.Invoke();
        }

        return Task.CompletedTask;
    }

    public Task<string?> InvokeScriptAsync(string script)
    {
        LastInvokeScriptThreadId = Environment.CurrentManagedThreadId;
        LastScript = script;

        if (ScriptException is not null)
        {
            return Task.FromException<string?>(ScriptException);
        }

        if (ScriptCallback is not null)
        {
            return Task.FromResult(ScriptCallback(script));
        }

        return Task.FromResult(ScriptResult);
    }

    public int AttachCallCount { get; private set; }
    public int DetachCallCount { get; private set; }
    public int StopCallCount { get; private set; }

    public bool GoBackAccepted { get; set; }
    public bool GoForwardAccepted { get; set; }
    public bool RefreshAccepted { get; set; }
    public bool StopAccepted { get; set; }

    public int GoBackCallCount { get; private set; }
    public int GoForwardCallCount { get; private set; }
    public int RefreshCallCount { get; private set; }

    public Guid? LastGoBackNavigationId { get; private set; }
    public Guid? LastGoForwardNavigationId { get; private set; }
    public Guid? LastRefreshNavigationId { get; private set; }

    public bool GoBack(Guid navigationId)
    {
        GoBackCallCount++;
        LastGoBackNavigationId = navigationId;
        LastNavigationId = navigationId;
        return GoBackAccepted;
    }

    public bool GoForward(Guid navigationId)
    {
        GoForwardCallCount++;
        LastGoForwardNavigationId = navigationId;
        LastNavigationId = navigationId;
        return GoForwardAccepted;
    }

    public bool Refresh(Guid navigationId)
    {
        RefreshCallCount++;
        LastRefreshNavigationId = navigationId;
        LastNavigationId = navigationId;
        return RefreshAccepted;
    }

    public bool Stop()
    {
        StopCallCount++;
        return StopAccepted;
    }

    public ValueTask<NativeNavigationStartingDecision> SimulateNativeNavigationStartingAsync(
        Uri requestUri,
        Guid? correlationId = null,
        bool isMainFrame = true)
    {
        if (_detached)
        {
            return ValueTask.FromResult(new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: Guid.Empty));
        }

        if (_host is null)
        {
            throw new InvalidOperationException("Initialize(IWebViewAdapterHost) must be called before simulating native navigation.");
        }

        var info = new NativeNavigationStartingInfo(
            CorrelationId: correlationId ?? Guid.NewGuid(),
            RequestUri: requestUri,
            IsMainFrame: isMainFrame);

        return InvokeAsync();

        async ValueTask<NativeNavigationStartingDecision> InvokeAsync()
        {
            var decision = await _host.OnNativeNavigationStartingAsync(info).ConfigureAwait(false);
            if (decision.IsAllowed && decision.NavigationId != Guid.Empty)
            {
                LastNavigationId = decision.NavigationId;
                LastNavigationUri = requestUri;
            }

            return decision;
        }
    }

    public void RaiseNavigationCompleted()
    {
        if (_detached)
        {
            return;
        }

        RaiseNavigationCompleted(NavigationCompletedStatus.Success);
    }

    public void RaiseNavigationCompleted(Guid navigationId, Uri requestUri, NavigationCompletedStatus status, Exception? error = null)
    {
        if (_detached)
        {
            return;
        }

        var args = new NavigationCompletedEventArgs(
            navigationId,
            requestUri,
            status,
            status == NavigationCompletedStatus.Failure ? error ?? new InvalidOperationException("Navigation failed.") : null);
        NavigationCompleted?.Invoke(this, args);
    }

    /// <summary>Raises NavigationCompleted with exact args (no auto-fill).</summary>
    public void RaiseNavigationCompletedRaw(NavigationCompletedEventArgs args)
    {
        if (_detached) return;
        NavigationCompleted?.Invoke(this, args);
    }

    public void RaiseNavigationCompleted(NavigationCompletedStatus status, Exception? error = null)
    {
        var navigationId = LastNavigationId ?? Guid.Empty;
        var requestUri = LastNavigationUri ?? new Uri("about:blank");
        RaiseNavigationCompleted(navigationId, requestUri, status, error);
    }

    public void RaiseNewWindowRequested(Uri? uri = null)
    {
        if (_detached) return;
        NewWindowRequested?.Invoke(this, new NewWindowRequestedEventArgs(uri));
    }

    public void RaiseWebMessage(string body, string origin, Guid channelId, int protocolVersion = 1)
    {
        if (_detached)
        {
            return;
        }

        WebMessageReceived?.Invoke(this, new WebMessageReceivedEventArgs(body, origin, channelId, protocolVersion));
    }

    public void RaiseWebResourceRequested()
    {
        if (_detached) return;
        WebResourceRequested?.Invoke(this, new WebResourceRequestedEventArgs());
    }

    public void RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
    {
        if (_detached) return;
        WebResourceRequested?.Invoke(this, args);
    }

    public void RaiseEnvironmentRequested()
    {
        if (_detached) return;
        EnvironmentRequested?.Invoke(this, new EnvironmentRequestedEventArgs());
    }

    protected static string CookieKey(string name, string domain, string path) => $"{name}|{domain}|{path}";

    /// <summary>Creates a mock that supports environment options.</summary>
    public static MockWebViewAdapterWithOptions CreateWithOptions() => new();

    /// <summary>Creates a mock that supports native handle provider.</summary>
    public static MockWebViewAdapterWithHandle CreateWithHandle() => new();

    /// <summary>Creates a mock that supports custom scheme registration.</summary>
    public static MockWebViewAdapterWithCustomSchemes CreateWithCustomSchemes() => new();

    /// <summary>Creates a mock that supports download events.</summary>
    public static MockWebViewAdapterWithDownload CreateWithDownload() => new();

    /// <summary>Creates a mock that supports permission events.</summary>
    public static MockWebViewAdapterWithPermission CreateWithPermission() => new();

    /// <summary>Creates a mock that supports all three new facets.</summary>
    public static MockWebViewAdapterFull CreateFull() => new();

    /// <summary>Creates a mock that supports editing commands.</summary>
    public static MockWebViewAdapterWithCommands CreateWithCommands() => new();

    /// <summary>Creates a mock that supports screenshot capture.</summary>
    public static MockWebViewAdapterWithScreenshot CreateWithScreenshot() => new();

    /// <summary>Creates a mock that supports PDF printing.</summary>
    public static MockWebViewAdapterWithPrint CreateWithPrint() => new();

    /// <summary>Creates a mock that supports find-in-page.</summary>
    public static MockWebViewAdapterWithFind CreateWithFind() => new();

    /// <summary>Creates a mock that supports zoom control.</summary>
    public static MockWebViewAdapterWithZoom CreateWithZoom() => new();

    /// <summary>Creates a mock that supports preload scripts.</summary>
    public static MockWebViewAdapterWithPreload CreateWithPreload() => new();

    /// <summary>Creates a mock that supports context menu interception.</summary>
    public static MockWebViewAdapterWithContextMenu CreateWithContextMenu() => new();

    /// <summary>Creates a mock that supports drag-and-drop events.</summary>
    public static MockWebViewAdapterWithDragDrop CreateWithDragDrop() => new();

    // -----------------------------------------------------------------------
    // Default no-op implementations for every MANDATORY capability facet that
    // IWebViewAdapter now inherits. Implemented explicitly so that derived
    // mocks re-declaring a facet interface (e.g. MockWebViewAdapterWithCookies
    // : MockWebViewAdapter, ICookieAdapter) can supply an observable public
    // override via ordinary interface-mapping, taking precedence over these
    // defaults without any `new`/`override` boilerplate.
    // -----------------------------------------------------------------------

    INativeHandle? INativeWebViewHandleProvider.TryGetWebViewHandle() => null;

    void IWebViewAdapterOptions.ApplyEnvironmentOptions(IWebViewEnvironmentOptions options) { }

    void IWebViewAdapterOptions.SetCustomUserAgent(string? userAgent) { }

    Task<IReadOnlyList<WebViewCookie>> ICookieAdapter.GetCookiesAsync(Uri uri)
        => Task.FromResult<IReadOnlyList<WebViewCookie>>([]);

    Task ICookieAdapter.SetCookieAsync(WebViewCookie cookie) => Task.CompletedTask;

    Task ICookieAdapter.DeleteCookieAsync(WebViewCookie cookie) => Task.CompletedTask;

    Task ICookieAdapter.ClearAllCookiesAsync() => Task.CompletedTask;

    void ICommandAdapter.ExecuteCommand(WebViewCommand command) { }

    void ICustomSchemeAdapter.RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes) { }

    event EventHandler<DownloadRequestedEventArgs>? IDownloadAdapter.DownloadRequested
    {
        add { }
        remove { }
    }

    event EventHandler<PermissionRequestedEventArgs>? IPermissionAdapter.PermissionRequested
    {
        add { }
        remove { }
    }

    Task<byte[]> IScreenshotAdapter.CaptureScreenshotAsync()
        => Task.FromResult(Array.Empty<byte>());

    Task<byte[]> IPrintAdapter.PrintToPdfAsync(PdfPrintOptions? options)
        => Task.FromResult(Array.Empty<byte>());

    Task<FindInPageEventArgs> IFindInPageAdapter.FindAsync(string text, FindInPageOptions? options)
        => Task.FromResult(new FindInPageEventArgs());

    void IFindInPageAdapter.StopFind(bool clearHighlights) { }

    double IZoomAdapter.ZoomFactor
    {
        get => 1.0;
        set { }
    }

    event EventHandler<double>? IZoomAdapter.ZoomFactorChanged
    {
        add { }
        remove { }
    }

    string IPreloadScriptAdapter.AddPreloadScript(string javaScript)
        => Guid.NewGuid().ToString("N");

    void IPreloadScriptAdapter.RemovePreloadScript(string scriptId) { }

    event EventHandler<ContextMenuRequestedEventArgs>? IContextMenuAdapter.ContextMenuRequested
    {
        add { }
        remove { }
    }

    void IDevToolsAdapter.OpenDevTools() { }

    void IDevToolsAdapter.CloseDevTools() { }

    bool IDevToolsAdapter.IsDevToolsOpen => false;
}

/// <summary>Mock adapter that also implements <see cref="IWebViewAdapterOptions"/> for environment options testing.</summary>
internal sealed class MockWebViewAdapterWithOptions : MockWebViewAdapter, IWebViewAdapterOptions
{
    public IWebViewEnvironmentOptions? AppliedOptions { get; private set; }
    public string? AppliedUserAgent { get; private set; }
    public int ApplyOptionsCallCount { get; private set; }
    public int SetUserAgentCallCount { get; private set; }

    public void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options)
    {
        ApplyOptionsCallCount++;
        AppliedOptions = options;
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        SetUserAgentCallCount++;
        AppliedUserAgent = userAgent;
    }
}

/// <summary>Mock adapter that also implements <see cref="INativeWebViewHandleProvider"/>.</summary>
internal sealed class MockWebViewAdapterWithHandle : MockWebViewAdapter, INativeWebViewHandleProvider
{
    public INativeHandle? HandleToReturn { get; set; }
    public int TryGetHandleCallCount { get; private set; }
    public int? LastTryGetHandleThreadId { get; private set; }

    public INativeHandle? TryGetWebViewHandle()
    {
        TryGetHandleCallCount++;
        LastTryGetHandleThreadId = Environment.CurrentManagedThreadId;
        return HandleToReturn;
    }
}

/// <summary>A test typed platform handle implementing <see cref="IWindowsWebView2PlatformHandle"/> for pattern-matching tests.</summary>
public sealed record TestWindowsWebView2PlatformHandle(nint Handle, nint CoreWebView2Handle, nint CoreWebView2ControllerHandle) : IWindowsWebView2PlatformHandle
{
    public string HandleDescriptor => "WebView2";
}

/// <summary>A test typed platform handle implementing <see cref="IAppleWKWebViewPlatformHandle"/> for pattern-matching tests.</summary>
public sealed record TestAppleWKWebViewPlatformHandle(nint WKWebViewHandle) : IAppleWKWebViewPlatformHandle
{
    public nint Handle => WKWebViewHandle;
    public string HandleDescriptor => "WKWebView";
}

/// <summary>A test typed platform handle implementing <see cref="IGtkWebViewPlatformHandle"/> for pattern-matching tests.</summary>
public sealed record TestGtkWebViewPlatformHandle(nint WebKitWebViewHandle) : IGtkWebViewPlatformHandle
{
    public nint Handle => WebKitWebViewHandle;
    public string HandleDescriptor => "WebKitGTK";
}

/// <summary>A test typed platform handle implementing <see cref="IAndroidWebViewPlatformHandle"/> for pattern-matching tests.</summary>
public sealed record TestAndroidWebViewPlatformHandle(nint AndroidWebViewHandle) : IAndroidWebViewPlatformHandle
{
    public nint Handle => AndroidWebViewHandle;
    public string HandleDescriptor => "AndroidWebView";
}

/// <summary>Mock adapter that also implements <see cref="ICustomSchemeAdapter"/> for custom scheme testing.</summary>
internal sealed class MockWebViewAdapterWithCustomSchemes : MockWebViewAdapter, ICustomSchemeAdapter
{
    public IReadOnlyList<CustomSchemeRegistration>? RegisteredSchemes { get; private set; }
    public int RegisterCallCount { get; private set; }

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        RegisterCallCount++;
        RegisteredSchemes = schemes;
    }
}

/// <summary>Mock adapter that also implements <see cref="IDownloadAdapter"/> for download event testing.</summary>
internal sealed class MockWebViewAdapterWithDownload : MockWebViewAdapter, IDownloadAdapter
{
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;

    public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
        => DownloadRequested?.Invoke(this, args);
}

/// <summary>Mock adapter that also implements <see cref="IPermissionAdapter"/> for permission event testing.</summary>
internal sealed class MockWebViewAdapterWithPermission : MockWebViewAdapter, IPermissionAdapter
{
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;

    public void RaisePermissionRequested(PermissionRequestedEventArgs args)
        => PermissionRequested?.Invoke(this, args);
}

/// <summary>Mock adapter that implements all new facets (ICustomSchemeAdapter, IDownloadAdapter, IPermissionAdapter).</summary>
internal sealed class MockWebViewAdapterFull : MockWebViewAdapter, ICustomSchemeAdapter, IDownloadAdapter, IPermissionAdapter
{
    public IReadOnlyList<CustomSchemeRegistration>? RegisteredSchemes { get; private set; }
    public int RegisterCallCount { get; private set; }
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        RegisterCallCount++;
        RegisteredSchemes = schemes;
    }

    public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
        => DownloadRequested?.Invoke(this, args);

    public void RaisePermissionRequested(PermissionRequestedEventArgs args)
        => PermissionRequested?.Invoke(this, args);
}

/// <summary>Mock adapter that also implements <see cref="ICommandAdapter"/> for command manager testing.</summary>
internal sealed class MockWebViewAdapterWithCommands : MockWebViewAdapter, ICommandAdapter
{
    public List<WebViewCommand> ExecutedCommands { get; } = new();

    public void ExecuteCommand(WebViewCommand command)
    {
        ExecutedCommands.Add(command);
    }
}

/// <summary>Mock adapter that also implements <see cref="IScreenshotAdapter"/> for screenshot testing.</summary>
internal sealed class MockWebViewAdapterWithScreenshot : MockWebViewAdapter, IScreenshotAdapter
{
    public byte[] ScreenshotResult { get; set; } = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic

    public Task<byte[]> CaptureScreenshotAsync()
    {
        return Task.FromResult(ScreenshotResult);
    }
}

/// <summary>Mock adapter that also implements <see cref="IPrintAdapter"/> for PDF printing testing.</summary>
internal sealed class MockWebViewAdapterWithPrint : MockWebViewAdapter, IPrintAdapter
{
    public byte[] PdfResult { get; set; } = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF magic

    /// <summary>The last options passed to <see cref="PrintToPdfAsync"/>.</summary>
    public PdfPrintOptions? LastPrintOptions { get; private set; }

    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
    {
        LastPrintOptions = options;
        return Task.FromResult(PdfResult);
    }
}

/// <summary>Mock adapter that also implements <see cref="IZoomAdapter"/> for zoom control testing.</summary>
internal sealed class MockWebViewAdapterWithZoom : MockWebViewAdapter, IZoomAdapter
{
    private double _zoomFactor = 1.0;

    public double ZoomFactor
    {
        get => _zoomFactor;
        set
        {
            _zoomFactor = value;
            ZoomFactorChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<double>? ZoomFactorChanged;

    /// <summary>Simulate an external zoom change (e.g. pinch-to-zoom).</summary>
    public void SimulateZoomChange(double newZoom) => ZoomFactor = newZoom;
}

/// <summary>Mock adapter that also implements <see cref="IFindInPageAdapter"/> for find-in-page testing.</summary>
internal sealed class MockWebViewAdapterWithFind : MockWebViewAdapter, IFindInPageAdapter
{
    /// <summary>The result to return from <see cref="FindAsync"/>.</summary>
    public FindInPageEventArgs FindResult { get; set; } = new() { ActiveMatchIndex = 0, TotalMatches = 3 };

    /// <summary>The last search text received.</summary>
    public string? LastSearchText { get; private set; }
    /// <summary>The last options received.</summary>
    public FindInPageOptions? LastOptions { get; private set; }
    /// <summary>Whether StopFind was called.</summary>
    public bool StopFindCalled { get; private set; }
    /// <summary>The clearHighlights parameter from the last StopFind call.</summary>
    public bool LastClearHighlights { get; private set; }

    public Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
    {
        LastSearchText = text;
        LastOptions = options;
        return Task.FromResult(FindResult);
    }

    public void StopFind(bool clearHighlights = true)
    {
        StopFindCalled = true;
        LastClearHighlights = clearHighlights;
    }
}

/// <summary>Mock adapter that also implements preload script adapters for preload script testing.</summary>
internal sealed class MockWebViewAdapterWithPreload : MockWebViewAdapter, IPreloadScriptAdapter, IAsyncPreloadScriptAdapter
{
    private int _nextId;
    public Dictionary<string, string> Scripts { get; } = new();
    public List<string> SyncAddedScripts { get; } = new();
    public List<string> AsyncAddedScripts { get; } = new();
    public List<string> SyncRemovedScriptIds { get; } = new();
    public List<string> AsyncRemovedScriptIds { get; } = new();

    public string AddPreloadScript(string javaScript)
    {
        SyncAddedScripts.Add(javaScript);
        var id = $"mock_preload_{++_nextId}";
        Scripts[id] = javaScript;
        return id;
    }

    public void RemovePreloadScript(string scriptId)
    {
        SyncRemovedScriptIds.Add(scriptId);
        Scripts.Remove(scriptId);
    }

    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        AsyncAddedScripts.Add(javaScript);
        var id = $"mock_preload_{++_nextId}";
        Scripts[id] = javaScript;
        return Task.FromResult(id);
    }

    public Task RemovePreloadScriptAsync(string scriptId)
    {
        AsyncRemovedScriptIds.Add(scriptId);
        Scripts.Remove(scriptId);
        return Task.CompletedTask;
    }
}

/// <summary>Mock adapter that also implements <see cref="ICookieAdapter"/> for cookie management testing.</summary>
internal sealed class MockWebViewAdapterWithCookies : MockWebViewAdapter, ICookieAdapter
{
    public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        var host = uri.Host;
        var result = CookieStore.Values
            .Where(c => c.Domain.EndsWith(host, StringComparison.OrdinalIgnoreCase) || host.EndsWith(c.Domain, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<WebViewCookie>>(result);
    }

    public Task SetCookieAsync(WebViewCookie cookie)
    {
        CookieStore[CookieKey(cookie.Name, cookie.Domain, cookie.Path)] = cookie;
        return Task.CompletedTask;
    }

    public Task DeleteCookieAsync(WebViewCookie cookie)
    {
        CookieStore.Remove(CookieKey(cookie.Name, cookie.Domain, cookie.Path));
        return Task.CompletedTask;
    }

    public Task ClearAllCookiesAsync()
    {
        CookieStore.Clear();
        return Task.CompletedTask;
    }
}

/// <summary>Mock adapter that also implements <see cref="IContextMenuAdapter"/> for context menu testing.</summary>
internal sealed class MockWebViewAdapterWithContextMenu : MockWebViewAdapter, IContextMenuAdapter
{
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>Simulates a context menu trigger for testing.</summary>
    public void RaiseContextMenu(ContextMenuRequestedEventArgs args)
    {
        ContextMenuRequested?.Invoke(this, args);
    }
}

/// <summary>Mock adapter that also implements <see cref="IDragDropAdapter"/> for drag-and-drop testing.</summary>
internal sealed class MockWebViewAdapterWithDragDrop : MockWebViewAdapter, IDragDropAdapter
{
    public event EventHandler<DragEventArgs>? DragEntered;
    public event EventHandler<DragEventArgs>? DragOver;
    public event EventHandler<EventArgs>? DragLeft;
    public event EventHandler<DropEventArgs>? DropCompleted;

    /// <summary>Simulates a drag entered event.</summary>
    public void RaiseDragEntered(DragEventArgs args) => DragEntered?.Invoke(this, args);

    /// <summary>Simulates a drag over event.</summary>
    public void RaiseDragOver(DragEventArgs args) => DragOver?.Invoke(this, args);

    /// <summary>Simulates a drag left event.</summary>
    public void RaiseDragLeft() => DragLeft?.Invoke(this, EventArgs.Empty);

    /// <summary>Simulates a drop completed event.</summary>
    public void RaiseDropCompleted(DropEventArgs args) => DropCompleted?.Invoke(this, args);
}
