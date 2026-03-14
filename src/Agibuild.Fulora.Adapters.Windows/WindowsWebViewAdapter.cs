using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace Agibuild.Fulora.Adapters.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWebViewAdapter : IWebViewAdapter, INativeWebViewHandleProvider, ICookieAdapter, IWebViewAdapterOptions, IDisposable,
    ICustomSchemeAdapter, IDownloadAdapter, IPermissionAdapter, ICommandAdapter, IScreenshotAdapter, IPrintAdapter,
    IFindInPageAdapter, IZoomAdapter, IPreloadScriptAdapter, IAsyncPreloadScriptAdapter, IContextMenuAdapter, IDevToolsAdapter,
    IDragDropAdapter
{
    private static bool DiagnosticsEnabled
        => string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal);

    private void Diag(string evt, string? detail = null)
    {
        if (!DiagnosticsEnabled) return;
        var tid = Environment.CurrentManagedThreadId;
        var uiTid = _uiThreadId;
        var hasWv = _webView is not null;
        var hasCtl = _controller is not null;
        var hasEnv = _environment is not null;
        var msg = detail is null ? "" : $" detail=\"{detail}\"";
        Console.WriteLine($"[Agibuild.WebView] {evt} tid={tid} uiTid={uiTid} attached={_attached} detached={_detached} webView={hasWv} controller={hasCtl} env={hasEnv}{msg}");
    }

    private IWebViewAdapterHost? _host;

    private bool _initialized;
    private bool _attached;
    private bool _detached;

    // WebView2 objects
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _webView;
    private IntPtr _parentHwnd;

    // Window subclass for resize tracking
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr _originalWndProc;

    // OLE drag-drop
    private DropTargetImpl? _dropTarget;

    // Readiness: Attach starts async init; operations queue until ready.
    private TaskCompletionSource? _readyTcs;
    private readonly Queue<Action> _pendingOps = new();
    private readonly object _pendingOpsLock = new();
    private CancellationTokenSource? _initCts;
    private int _teardownStarted;
    private int _teardownCompleted;
    private int _uiThreadId;

    // The SynchronizationContext captured during WebView2 initialization (UI thread).
    // All COM calls must be dispatched to this context.
    private SynchronizationContext? _uiSyncContext;

    // Navigation state
    private readonly object _navLock = new();

    // Maps WebView2 NavigationId (ulong) → our CorrelationId (Guid).
    private readonly Dictionary<ulong, Guid> _correlationMap = new();

    // Maps WebView2 NavigationId (ulong) → host-issued or API NavigationId (Guid).
    private readonly Dictionary<ulong, Guid> _navigationIdMap = new();

    // Tracks the request URI for each WebView2 NavigationId (set in NavigationStarting).
    private readonly Dictionary<ulong, Uri> _requestUriMap = new();

    // WebView2 NavigationIds that originated from adapter API calls (skip host callback).
    private readonly HashSet<ulong> _apiNavIds = new();

    // Set when an API navigation is about to start; the next NavigationStarting event picks it up.
    private Guid _pendingApiNavigationId;
    private bool _pendingApiNavigation;
    private Guid _lastApiNavigationId;
    private Uri? _lastApiNavigationRequestUri;
    private long _lastApiNavigationStartedAtMs;

    // Guard exactly-once completion per NavigationId.
    private readonly HashSet<Guid> _completedNavIds = new();

    // NavigateToString + baseUrl intercept state
    private string? _pendingBaseUrlHtml;
    private Uri? _pendingBaseUrl;
    private Guid _pendingBaseUrlNavId;

    // Environment options (stored before Attach, applied after WebView2 init)
    private IWebViewEnvironmentOptions? _pendingOptions;

    // Custom scheme registrations (stored before Attach, applied during env creation)
    private IReadOnlyList<CustomSchemeRegistration>? _customSchemes;

    public bool CanGoBack => _webView?.CanGoBack ?? false;
    public bool CanGoForward => _webView?.CanGoForward ?? false;

    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;

    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add { }
        remove { }
    }

    // ==================== IDragDropAdapter ====================

    public event EventHandler<DragEventArgs>? DragEntered;
    public event EventHandler<DragEventArgs>? DragOver;
    public event EventHandler<EventArgs>? DragLeft;
    public event EventHandler<DropEventArgs>? DropCompleted;

    // ==================== ICustomSchemeAdapter ====================

    public void RegisterCustomSchemes(IReadOnlyList<CustomSchemeRegistration> schemes)
    {
        _customSchemes = schemes;
    }

    // ==================== Lifecycle ====================

    public void Initialize(IWebViewAdapterHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (_initialized)
        {
            throw new InvalidOperationException($"{nameof(Initialize)} can only be called once.");
        }

        _initialized = true;
        _host = host;
    }

    public void Attach(INativeHandle parentHandle)
    {
        ArgumentNullException.ThrowIfNull(parentHandle);
        ThrowIfNotInitialized();

        if (_detached)
        {
            throw new InvalidOperationException($"{nameof(Attach)} cannot be called after {nameof(Detach)}.");
        }

        if (_attached)
        {
            throw new InvalidOperationException($"{nameof(Attach)} can only be called once.");
        }

        if (parentHandle.Handle == IntPtr.Zero)
        {
            throw new ArgumentException("Parent handle must be non-zero.", nameof(parentHandle));
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WebView2 adapter can only be used on Windows.");
        }

        _attached = true;
        _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _initCts = new CancellationTokenSource();
        _uiSyncContext ??= SynchronizationContext.Current;
        _uiThreadId = Environment.CurrentManagedThreadId;
        Diag("Attach: start", $"parentHwnd=0x{parentHandle.Handle.ToInt64():x}");

        _ = InitializeWebView2Async(parentHandle.Handle, _initCts.Token);
    }

    public void Detach()
    {
        ThrowIfNotInitialized();

        if (_detached)
        {
            return;
        }

        _detached = true;
        _attached = false;
        Interlocked.Exchange(ref _teardownStarted, 1);
        Diag("Detach: start");

        try
        {
            try
            {
                _initCts?.Cancel();
            }
            catch
            {
                // Best effort
            }
            finally
            {
                _initCts?.Dispose();
                _initCts = null;
            }

            // Detach must be safe regardless of caller thread. We do all WebView2/Win32 teardown
            // on the captured UI SynchronizationContext when available, and avoid indefinite waits.
            TearDownWebView2BestEffort();
            Diag("Detach: teardown returned");

            _readyTcs?.TrySetCanceled();
        }
        finally
        {
            lock (_navLock)
            {
                _correlationMap.Clear();
                _navigationIdMap.Clear();
                _apiNavIds.Clear();
                _completedNavIds.Clear();
                _requestUriMap.Clear();
                _pendingApiNavigation = false;
            }

            lock (_pendingOpsLock)
            {
                _pendingOps.Clear();
            }
            Diag("Detach: completed");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Detach();
    }

    private async Task InitializeWebView2Async(IntPtr parentHwnd, CancellationToken ct)
    {
        try
        {
            if (DiagnosticsEnabled)
            {
                Console.WriteLine("[Agibuild.WebView] WebView2 initializing...");
            }

            _environment = await CoreWebView2Environment.CreateAsync().ConfigureAwait(true);
            if (_detached || ct.IsCancellationRequested)
            {
                // Detach requested while initializing: ensure we don't leak WebView2 objects.
                TearDownWebView2OnInitThread();
                _readyTcs?.TrySetCanceled(ct);
                return;
            }

            _controller = await _environment.CreateCoreWebView2ControllerAsync(parentHwnd).ConfigureAwait(true);
            _webView = _controller.CoreWebView2;
            _parentHwnd = parentHwnd;
            _uiSyncContext ??= SynchronizationContext.Current;
            _uiThreadId = Environment.CurrentManagedThreadId;

            if (_detached || ct.IsCancellationRequested)
            {
                TearDownWebView2OnInitThread();
                _readyTcs?.TrySetCanceled(ct);
                return;
            }

            // Size the controller to fill the parent window and track future resizes.
            UpdateControllerBounds();
            _controller.IsVisible = true;
            SubclassParentWindow();
            RegisterDropTarget();

            // Apply pending environment options
            ApplyPendingOptions();

            // Subscribe to events
            _webView.NavigationStarting += OnNavigationStarting;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.NewWindowRequested += OnNewWindowRequested;
            _webView.WebMessageReceived += OnWebMessageReceived;
            _webView.WebResourceRequested += OnWebResourceRequested;
            _webView.DownloadStarting += OnDownloadStarting;
            _webView.PermissionRequested += OnPermissionRequested;

            // Register custom scheme filters for WebResourceRequested
            if (_customSchemes is { Count: > 0 })
            {
                foreach (var scheme in _customSchemes)
                {
                    var filter = $"{scheme.SchemeName}://*";
                    _webView.AddWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All);
                }
            }

            // Inject WebMessage channel routing script
            var channelId = _host?.ChannelId ?? Guid.Empty;
            var bridgeScript = WebViewBridgeScriptFactory.CreateWindowsBridgeBootstrapScript(channelId);
            await _webView.AddScriptToExecuteOnDocumentCreatedAsync(bridgeScript).ConfigureAwait(true);

            // Replay queued operations
            if (_detached || ct.IsCancellationRequested)
            {
                TearDownWebView2OnInitThread();
                _readyTcs?.TrySetCanceled(ct);
                return;
            }

            List<Action>? opsToReplay = null;
            lock (_pendingOpsLock)
            {
                if (_pendingOps.Count > 0)
                {
                    opsToReplay = new List<Action>(_pendingOps);
                    _pendingOps.Clear();
                }
            }

            if (opsToReplay is not null)
            {
                foreach (var op in opsToReplay)
                {
                    if (_detached || ct.IsCancellationRequested || Volatile.Read(ref _teardownStarted) == 1)
                        break;
                    op();
                }
            }

            if (DiagnosticsEnabled)
            {
                Console.WriteLine("[Agibuild.WebView] WebView2 initialized successfully.");
            }

            _readyTcs?.TrySetResult();
        }
        catch (Exception ex)
        {
            if (_detached || ct.IsCancellationRequested)
            {
                TearDownWebView2OnInitThread();
                _readyTcs?.TrySetCanceled(ct);
                return;
            }

            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] WebView2 initialization failed: {ex.Message}");
            }

            _readyTcs?.TrySetException(ex);
        }
    }

    private void TearDownWebView2BestEffort()
    {
        if (Volatile.Read(ref _teardownCompleted) == 1)
        {
            return;
        }

        Diag("Teardown: start");

        // SynchronizationContext instances can differ even on the same UI thread.
        // Rely on thread identity as the authoritative signal to avoid self-post + wait timeout.
        if (_uiSyncContext is null ||
            SynchronizationContext.Current == _uiSyncContext ||
            Environment.CurrentManagedThreadId == _uiThreadId)
        {
            TearDownWebView2OnInitThread();
            Interlocked.Exchange(ref _teardownCompleted, 1);
            Diag("Teardown: done (inline)");
            return;
        }

        using var completed = new ManualResetEventSlim(false);
        _uiSyncContext.Post(_ =>
        {
            try
            {
                Diag("Teardown: on-ui begin");
                TearDownWebView2OnInitThread();
            }
            catch
            {
                // Best effort
            }
            finally
            {
                Interlocked.Exchange(ref _teardownCompleted, 1);
                Diag("Teardown: on-ui end");
                completed.Set();
            }
        }, null);

        // Avoid deadlocks when UI thread is already exiting.
        var ok = completed.Wait(TimeSpan.FromSeconds(5));
        if (!ok)
        {
            Diag("Teardown: wait timed out");
        }
    }

    /// <summary>
    /// Tears down WebView2 objects on the UI thread / init thread.
    /// This is safe to call multiple times.
    /// </summary>
    private void TearDownWebView2OnInitThread()
    {
        Diag("Teardown: core begin");
        try
        {
            UnregisterDropTarget();
            RestoreParentWindowProc();

            if (_webView is not null)
            {
                _webView.NavigationStarting -= OnNavigationStarting;
                _webView.NavigationCompleted -= OnNavigationCompleted;
                _webView.NewWindowRequested -= OnNewWindowRequested;
                _webView.WebMessageReceived -= OnWebMessageReceived;
                _webView.WebResourceRequested -= OnWebResourceRequested;
                _webView.DownloadStarting -= OnDownloadStarting;
                _webView.PermissionRequested -= OnPermissionRequested;
            }

            _controller?.Close();
        }
        finally
        {
            var webView = _webView;
            var controller = _controller;
            var environment = _environment;

            _webView = null;
            _controller = null;
            _environment = null;
            _uiSyncContext = null;
            _parentHwnd = IntPtr.Zero;

            ReleaseComObject(webView);
            ReleaseComObject(controller);
            ReleaseComObject(environment);
        }
        Diag("Teardown: core end");
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj is null) return;

        try
        {
            if (Marshal.IsComObject(obj))
            {
                Marshal.FinalReleaseComObject(obj);
            }
        }
        catch
        {
            // Best effort: COM release should never crash cleanup.
        }
    }

    private void ApplyPendingOptions()
    {
        if (_webView is null || _pendingOptions is null) return;

        _webView.Settings.AreDevToolsEnabled = _pendingOptions.EnableDevTools;

        if (_pendingOptions.CustomUserAgent is not null)
        {
            _webView.Settings.UserAgent = _pendingOptions.CustomUserAgent;
        }
    }

    // ==================== Navigation — API-initiated ====================

    public Task NavigateAsync(Guid navigationId, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ThrowIfNotAttached();

        lock (_navLock) { BeginApiNavigation(navigationId); }

        ExecuteOrQueue(() => _webView!.Navigate(uri.AbsoluteUri));
        return Task.CompletedTask;
    }

    public Task NavigateToStringAsync(Guid navigationId, string html)
        => NavigateToStringAsync(navigationId, html, baseUrl: null);

    public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        ThrowIfNotAttached();

        lock (_navLock) { BeginApiNavigation(navigationId); }

        if (baseUrl is null)
        {
            ExecuteOrQueue(() => _webView!.NavigateToString(html));
        }
        else
        {
            // Use WebResourceRequested intercept to serve HTML at baseUrl origin.
            _pendingBaseUrlHtml = html;
            _pendingBaseUrl = baseUrl;
            _pendingBaseUrlNavId = navigationId;

            ExecuteOrQueue(() =>
            {
                var uri = _pendingBaseUrl!.AbsoluteUri;
                _webView!.AddWebResourceRequestedFilter(uri, CoreWebView2WebResourceContext.All);
                _webView.Navigate(uri);
            });
        }

        return Task.CompletedTask;
    }

    // ==================== Navigation — native-initiated interception ====================

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_detached) return;

        var wv2NavId = e.NavigationId;

        lock (_navLock)
        {
            // Check if this is from an API-initiated navigation.
            if (_pendingApiNavigation)
            {
                _pendingApiNavigation = false;
                _apiNavIds.Add(wv2NavId);
                _navigationIdMap[wv2NavId] = _pendingApiNavigationId;
                // Create correlation entry using the API NavigationId as CorrelationId.
                _correlationMap[wv2NavId] = _pendingApiNavigationId;

                if (!string.IsNullOrWhiteSpace(e.Uri) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var apiUri))
                {
                    _requestUriMap[wv2NavId] = apiUri;
                    _lastApiNavigationRequestUri = apiUri;
                }
                else
                {
                    _lastApiNavigationRequestUri = null;
                }
                _lastApiNavigationId = _pendingApiNavigationId;
                _lastApiNavigationStartedAtMs = Environment.TickCount64;

                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] NavigationStarting (API): wv2Id={wv2NavId}, navId={_pendingApiNavigationId}, uri={e.Uri}");
                }

                return; // Allow API navigation to proceed.
            }

            // WebView2 may emit an additional non-user-initiated start for the same
            // adapter-initiated navigation (for example around about:blank transitions),
            // sometimes with a different WebView2 navigation id. Treat it as the same
            // API navigation instead of re-entering native host arbitration.
            if (TryMapApiContinuationNavigationLocked(wv2NavId, e))
            {
                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] NavigationStarting (API continuation): wv2Id={wv2NavId}, uri={e.Uri}");
                }

                return;
            }

            // Check if this is a redirect for an already-tracked navigation.
            if (_apiNavIds.Contains(wv2NavId))
            {
                // Update the URI for this redirect.
                if (!string.IsNullOrWhiteSpace(e.Uri) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var redirectUri))
                {
                    _requestUriMap[wv2NavId] = redirectUri;
                }

                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] NavigationStarting (API redirect): wv2Id={wv2NavId}, uri={e.Uri}");
                }

                return; // Allow redirect for API navigation.
            }
        }

        // Native-initiated navigation — consult the host.
        if (string.IsNullOrWhiteSpace(e.Uri) || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var requestUri))
        {
            return; // Allow navigations with unparseable URIs.
        }

        var host = _host;
        if (host is null)
        {
            e.Cancel = true;
            return;
        }

        Guid correlationId;
        lock (_navLock)
        {
            if (_correlationMap.TryGetValue(wv2NavId, out var existingCorrelation))
            {
                // Redirect in same chain.
                correlationId = existingCorrelation;
            }
            else
            {
                // New native navigation chain.
                correlationId = Guid.NewGuid();
                _correlationMap[wv2NavId] = correlationId;
            }
        }

        var info = new NativeNavigationStartingInfo(correlationId, requestUri, IsMainFrame: true);
        var decisionTask = host.OnNativeNavigationStartingAsync(info);

        // WebView2 events run on UI thread; the host implementation completes synchronously
        // on UI thread. If not, fall back to blocking (should not happen in practice).
        var decision = decisionTask.IsCompleted
            ? decisionTask.Result
            : decisionTask.AsTask().GetAwaiter().GetResult();

        if (DiagnosticsEnabled)
        {
            Console.WriteLine($"[Agibuild.WebView] NavigationStarting (native): wv2Id={wv2NavId}, uri={e.Uri}, allowed={decision.IsAllowed}, navId={decision.NavigationId}");
        }

        if (!decision.IsAllowed)
        {
            e.Cancel = true;

            if (decision.NavigationId != Guid.Empty)
            {
                // Report canceled completion for the denied navigation.
                RaiseNavigationCompleted(decision.NavigationId, requestUri, NavigationCompletedStatus.Canceled, error: null);
            }

            lock (_navLock)
            {
                _correlationMap.Remove(wv2NavId);
            }

            return;
        }

        if (decision.NavigationId != Guid.Empty)
        {
            lock (_navLock)
            {
                _navigationIdMap[wv2NavId] = decision.NavigationId;
                _requestUriMap[wv2NavId] = requestUri;
            }
        }
    }

    private bool TryMapApiContinuationNavigationLocked(ulong wv2NavId, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri) || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var requestUri))
        {
            return false;
        }

        Guid matchedApiNavigationId = Guid.Empty;
        var matched = false;

        foreach (var apiWv2NavId in _apiNavIds)
        {
            if (!_navigationIdMap.TryGetValue(apiWv2NavId, out var apiNavigationId))
            {
                continue;
            }

            if (!_requestUriMap.TryGetValue(apiWv2NavId, out var trackedUri))
            {
                continue;
            }

            if (!string.Equals(trackedUri.AbsoluteUri, requestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchedApiNavigationId = apiNavigationId;
            matched = true;
            break;
        }

        if (matched)
        {
            _apiNavIds.Add(wv2NavId);
            _navigationIdMap[wv2NavId] = matchedApiNavigationId;
            _correlationMap[wv2NavId] = matchedApiNavigationId;
            _requestUriMap[wv2NavId] = requestUri;
            return true;
        }

        if (Environment.TickCount64 - _lastApiNavigationStartedAtMs > 2_000)
        {
            return false;
        }

        if (TryGetSingleInFlightApiNavigationIdLocked(out var singleApiNavigationId) &&
            _lastApiNavigationRequestUri is not null &&
            string.Equals(_lastApiNavigationRequestUri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            _apiNavIds.Add(wv2NavId);
            _navigationIdMap[wv2NavId] = singleApiNavigationId;
            _correlationMap[wv2NavId] = singleApiNavigationId;
            _requestUriMap[wv2NavId] = requestUri;
            return true;
        }

        if (_lastApiNavigationId != Guid.Empty &&
            _lastApiNavigationRequestUri is not null &&
            string.Equals(_lastApiNavigationRequestUri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            _apiNavIds.Add(wv2NavId);
            _navigationIdMap[wv2NavId] = _lastApiNavigationId;
            _correlationMap[wv2NavId] = _lastApiNavigationId;
            _requestUriMap[wv2NavId] = requestUri;
            return true;
        }

        return false;
    }

    private bool TryGetSingleInFlightApiNavigationIdLocked(out Guid navigationId)
    {
        navigationId = Guid.Empty;
        var hasValue = false;

        foreach (var apiWv2NavId in _apiNavIds)
        {
            if (!_navigationIdMap.TryGetValue(apiWv2NavId, out var mappedNavigationId))
            {
                continue;
            }

            if (!hasValue)
            {
                navigationId = mappedNavigationId;
                hasValue = true;
                continue;
            }

            if (navigationId != mappedNavigationId)
            {
                navigationId = Guid.Empty;
                return false;
            }
        }

        return hasValue;
    }

    // ==================== Navigation — completion and error mapping ====================

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_detached) return;

        var wv2NavId = e.NavigationId;

        // Remove the baseUrl intercept filter if this was a baseUrl navigation.
        if (_pendingBaseUrl is not null && _pendingBaseUrlHtml is not null)
        {
            var baseUri = _pendingBaseUrl.AbsoluteUri;
            try { _webView?.RemoveWebResourceRequestedFilter(baseUri, CoreWebView2WebResourceContext.All); }
            catch { /* Filter may already be removed. */ }
            _pendingBaseUrlHtml = null;
            _pendingBaseUrl = null;
        }

        Guid navigationId;
        Uri? trackedUri;
        var deferConnectionAbortedCompletion = false;
        lock (_navLock)
        {
            if (!_navigationIdMap.TryGetValue(wv2NavId, out navigationId))
            {
                // Untracked navigation (subframe or ignored); clean up and return.
                _correlationMap.Remove(wv2NavId);
                _apiNavIds.Remove(wv2NavId);
                _requestUriMap.Remove(wv2NavId);
                return;
            }

            // If this is a transient abort while the same logical navigation still has
            // another tracked WebView2 navigation id, defer completion and wait for the
            // sibling completion to determine final status.
            deferConnectionAbortedCompletion =
                !e.IsSuccess &&
                e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted &&
                HasSiblingNavigationMappingLocked(navigationId, wv2NavId);

            // Retrieve tracked URI before cleanup.
            _requestUriMap.TryGetValue(wv2NavId, out trackedUri);

            // Clean up current WebView2 navigation id state.
            _navigationIdMap.Remove(wv2NavId);
            _correlationMap.Remove(wv2NavId);
            _apiNavIds.Remove(wv2NavId);
            _requestUriMap.Remove(wv2NavId);

            if (deferConnectionAbortedCompletion)
            {
                return;
            }

            // Exactly-once guard.
            if (!_completedNavIds.Add(navigationId))
            {
                return;
            }
        }

        var requestUri = trackedUri ?? new Uri("about:blank");

        if (e.IsSuccess)
        {
            RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Success, error: null);
            return;
        }

        var status = e.WebErrorStatus;

        if (status == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Canceled, error: null);
            return;
        }

        if (status == CoreWebView2WebErrorStatus.ConnectionAborted &&
            string.Equals(requestUri.AbsoluteUri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            // WebView2 may report an intermediate about:blank navigation as aborted
            // while transitioning into the final document (e.g., NavigateToString/baseUrl flows).
            // Do not surface this as a hard failure to callers.
            RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Canceled, error: null);
            return;
        }

        var errorMessage = $"Navigation failed: {status}";
        Exception error = MapWebErrorStatus(status, errorMessage, navigationId, requestUri);

        RaiseNavigationCompleted(navigationId, requestUri, NavigationCompletedStatus.Failure, error);
    }

    private bool HasSiblingNavigationMappingLocked(Guid navigationId, ulong excludeWv2NavigationId)
    {
        foreach (var kvp in _navigationIdMap)
        {
            if (kvp.Key == excludeWv2NavigationId)
            {
                continue;
            }

            if (kvp.Value == navigationId)
            {
                return true;
            }
        }

        return false;
    }

    private static Exception MapWebErrorStatus(CoreWebView2WebErrorStatus status, string message, Guid navigationId, Uri requestUri)
    {
        var category = status switch
        {
            CoreWebView2WebErrorStatus.Timeout
                => NavigationErrorCategory.Timeout,

            CoreWebView2WebErrorStatus.ConnectionAborted or
            CoreWebView2WebErrorStatus.ConnectionReset or
            CoreWebView2WebErrorStatus.Disconnected or
            CoreWebView2WebErrorStatus.CannotConnect or
            CoreWebView2WebErrorStatus.HostNameNotResolved
                => NavigationErrorCategory.Network,

            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
            CoreWebView2WebErrorStatus.CertificateExpired or
            CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
            CoreWebView2WebErrorStatus.CertificateRevoked or
            CoreWebView2WebErrorStatus.CertificateIsInvalid
                => NavigationErrorCategory.Ssl,

            _ => NavigationErrorCategory.Other,
        };

        return NavigationErrorFactory.Create(category, message, navigationId, requestUri);
    }

    // ==================== Navigation — commands ====================

    public bool GoBack(Guid navigationId)
    {
        ThrowIfNotAttached();
        return RunOnUiThread(() =>
        {
            if (_webView is null || !_webView.CanGoBack) return false;
            lock (_navLock) { BeginApiNavigation(navigationId); }
            _webView.GoBack();
            return true;
        });
    }

    public bool GoForward(Guid navigationId)
    {
        ThrowIfNotAttached();
        return RunOnUiThread(() =>
        {
            if (_webView is null || !_webView.CanGoForward) return false;
            lock (_navLock) { BeginApiNavigation(navigationId); }
            _webView.GoForward();
            return true;
        });
    }

    public bool Refresh(Guid navigationId)
    {
        ThrowIfNotAttached();
        return RunOnUiThread(() =>
        {
            if (_webView is null) return false;
            lock (_navLock) { BeginApiNavigation(navigationId); }
            _webView.Reload();
            return true;
        });
    }

    public bool Stop()
    {
        ThrowIfNotAttached();
        RunOnUiThread(() => _webView?.Stop());
        return true;
    }

    // ==================== Script execution ====================

    public async Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfNotAttached();

        if (_webView is null)
        {
            // Wait for WebView2 to be ready.
            if (_readyTcs is not null)
            {
                await _readyTcs.Task.ConfigureAwait(false);
            }

            if (_webView is null)
            {
                throw new InvalidOperationException("WebView2 is not available.");
            }
        }

        var jsonResult = await RunOnUiThreadAsync(async () =>
            await _webView.ExecuteScriptAsync(script).ConfigureAwait(true)
        ).ConfigureAwait(false);

        // WebView2 returns JSON-encoded results — normalize to raw values per V1 contract.
        return ScriptResultHelper.NormalizeJsonResult(jsonResult);
    }

    // ==================== WebMessage bridge ====================

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_detached) return;

        var channelId = _host?.ChannelId ?? Guid.Empty;
        var body = e.TryGetWebMessageAsString();
        var origin = e.Source ?? string.Empty;

        // Try to parse our structured message envelope.
        // If not parseable, forward the raw body.
        SafeRaise(() => WebMessageReceived?.Invoke(this, new WebMessageReceivedEventArgs(
            body ?? string.Empty, origin, channelId, protocolVersion: 1)));
    }

    // ==================== NewWindowRequested ====================

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_detached) return;

        Uri? targetUri = null;
        if (!string.IsNullOrWhiteSpace(e.Uri))
        {
            Uri.TryCreate(e.Uri, UriKind.Absolute, out targetUri);
        }

        var args = new NewWindowRequestedEventArgs(targetUri);
        SafeRaise(() => NewWindowRequested?.Invoke(this, args));

        // Always mark as handled to prevent WebView2 from opening a new window.
        e.Handled = true;
    }

    // ==================== WebResourceRequested ====================

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_detached) return;

        // Handle baseUrl intercept first (internal).
        if (_pendingBaseUrlHtml is not null && _pendingBaseUrl is not null)
        {
            var requestedUri = e.Request.Uri;
            if (string.Equals(requestedUri, _pendingBaseUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            {
                var html = _pendingBaseUrlHtml;
                // Do not dispose the stream — WebView2 reads it asynchronously after this method returns.
                var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
                var response = _webView!.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
                e.Response = response;
                return;
            }
        }

        // Raise the public WebResourceRequested event for custom scheme requests.
        Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerIter = e.Request.Headers.GetEnumerator();
        while (headerIter.MoveNext())
        {
            headers[headerIter.Current.Key] = headerIter.Current.Value;
        }

        var args = new WebResourceRequestedEventArgs(uri!, e.Request.Method, headers);
        SafeRaise(() => WebResourceRequested?.Invoke(this, args));

        if (args.Handled && args.ResponseBody is not null)
        {
            var headerBuilder = $"Content-Type: {args.ResponseContentType}";
            if (args.ResponseHeaders is { Count: > 0 })
            {
                foreach (var kvp in args.ResponseHeaders)
                {
                    headerBuilder += $"\r\n{kvp.Key}: {kvp.Value}";
                }
            }

            var response = _webView!.Environment.CreateWebResourceResponse(
                args.ResponseBody, args.ResponseStatusCode, "OK", headerBuilder);
            e.Response = response;
        }
    }

    // ==================== IDownloadAdapter ====================

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (_detached) return;

        var uri = new Uri(e.DownloadOperation.Uri);
        var args = new DownloadRequestedEventArgs(
            uri,
            e.ResultFilePath != null ? System.IO.Path.GetFileName(e.ResultFilePath) : null,
            e.DownloadOperation.MimeType,
            e.DownloadOperation.TotalBytesToReceive > 0 ? (long)e.DownloadOperation.TotalBytesToReceive : null);

        SafeRaise(() => DownloadRequested?.Invoke(this, args));

        if (args.Cancel)
        {
            e.Cancel = true;
        }
        else if (!string.IsNullOrEmpty(args.DownloadPath))
        {
            e.ResultFilePath = args.DownloadPath;
        }

        e.Handled = args.Handled;
    }

    // ==================== IPermissionAdapter ====================

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (_detached) return;

        var kind = MapPermissionKind(e.PermissionKind);
        Uri.TryCreate(e.Uri, UriKind.Absolute, out var origin);

        var args = new PermissionRequestedEventArgs(kind, origin);
        SafeRaise(() => PermissionRequested?.Invoke(this, args));

        e.State = args.State switch
        {
            PermissionState.Allow => CoreWebView2PermissionState.Allow,
            PermissionState.Deny => CoreWebView2PermissionState.Deny,
            _ => CoreWebView2PermissionState.Default
        };
    }

    private static WebViewPermissionKind MapPermissionKind(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => WebViewPermissionKind.Camera,
        CoreWebView2PermissionKind.Microphone => WebViewPermissionKind.Microphone,
        CoreWebView2PermissionKind.Geolocation => WebViewPermissionKind.Geolocation,
        CoreWebView2PermissionKind.Notifications => WebViewPermissionKind.Notifications,
        CoreWebView2PermissionKind.ClipboardRead => WebViewPermissionKind.ClipboardRead,
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => WebViewPermissionKind.Midi,
        _ => WebViewPermissionKind.Other
    };

    // ==================== ICookieAdapter ====================

    public async Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        ThrowIfNotAttachedForCookies();
        return await RunOnUiThreadAsync(async () =>
        {
            var cookieManager = _webView!.CookieManager;
            // Must stay on UI thread — CoreWebView2Cookie COM objects have thread affinity.
            var cookies = await cookieManager.GetCookiesAsync(uri.AbsoluteUri).ConfigureAwait(true);

            var result = new List<WebViewCookie>(cookies.Count);
            for (var i = 0; i < cookies.Count; i++)
            {
                var c = cookies[i];
                DateTimeOffset? expires = c.Expires.Year > 1970
                    ? new DateTimeOffset(c.Expires)
                    : null;

                result.Add(new WebViewCookie(
                    c.Name, c.Value, c.Domain, c.Path,
                    expires, c.IsSecure, c.IsHttpOnly));
            }

            return (IReadOnlyList<WebViewCookie>)result;
        }).ConfigureAwait(false);
    }

    public Task SetCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        RunOnUiThread(() =>
        {
            var cookieManager = _webView!.CookieManager;
            var wv2Cookie = cookieManager.CreateCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path);

            if (cookie.Expires.HasValue)
            {
                wv2Cookie.Expires = cookie.Expires.Value.UtcDateTime;
            }

            wv2Cookie.IsSecure = cookie.IsSecure;
            wv2Cookie.IsHttpOnly = cookie.IsHttpOnly;

            cookieManager.AddOrUpdateCookie(wv2Cookie);
        });
        return Task.CompletedTask;
    }

    public async Task DeleteCookieAsync(WebViewCookie cookie)
    {
        ThrowIfNotAttachedForCookies();
        await RunOnUiThreadAsync(async () =>
        {
            var cookieManager = _webView!.CookieManager;

            // Find matching cookie(s) by name, domain, path — then delete.
            // Must stay on UI thread — CoreWebView2Cookie COM objects have thread affinity.
            var allCookies = await cookieManager.GetCookiesAsync($"https://{cookie.Domain}{cookie.Path}").ConfigureAwait(true);
            for (var i = 0; i < allCookies.Count; i++)
            {
                var c = allCookies[i];
                if (string.Equals(c.Name, cookie.Name, StringComparison.Ordinal) &&
                    string.Equals(c.Domain, cookie.Domain, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Path, cookie.Path, StringComparison.Ordinal))
                {
                    cookieManager.DeleteCookie(c);
                }
            }

            return true; // satisfy RunOnUiThreadAsync<T> signature
        }).ConfigureAwait(false);
    }

    public Task ClearAllCookiesAsync()
    {
        ThrowIfNotAttachedForCookies();
        RunOnUiThread(() => _webView!.CookieManager.DeleteAllCookies());
        return Task.CompletedTask;
    }

    // ==================== INativeWebViewHandleProvider ====================

    public INativeHandle? TryGetWebViewHandle()
    {
        if (!_attached || _detached || _controller is null || _webView is null) return null;

        return RunOnUiThread(() =>
        {
            if (_controller is null || _webView is null) return null;
            var hwnd = _controller.ParentWindow;
            if (hwnd == IntPtr.Zero) return null;

            // Marshal.GetIUnknownForObject returns a ref-counted COM pointer.
            var coreWebView2Ptr = Marshal.GetIUnknownForObject(_webView);
            var controllerPtr = Marshal.GetIUnknownForObject(_controller);
            return (INativeHandle?)new WindowsWebView2PlatformHandle(hwnd, coreWebView2Ptr, controllerPtr);
        });
    }

    /// <summary>Typed platform handle for Windows WebView2.</summary>
    private sealed record WindowsWebView2PlatformHandle(nint Handle, nint CoreWebView2Handle, nint CoreWebView2ControllerHandle) : IWindowsWebView2PlatformHandle
    {
        public string HandleDescriptor => "WebView2";
    }

    // ==================== IWebViewAdapterOptions ====================

    public void ApplyEnvironmentOptions(IWebViewEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfNotInitialized();

        if (_webView is not null)
        {
            RunOnUiThread(() =>
            {
                _webView.Settings.AreDevToolsEnabled = options.EnableDevTools;
                if (options.CustomUserAgent is not null)
                {
                    _webView.Settings.UserAgent = options.CustomUserAgent;
                }
            });
        }
        else
        {
            // Store for later application after WebView2 init.
            _pendingOptions = options;
        }
    }

    public void SetCustomUserAgent(string? userAgent)
    {
        ThrowIfNotInitialized();
        if (_webView is not null)
        {
            RunOnUiThread(() => _webView.Settings.UserAgent = userAgent ?? string.Empty);
        }
    }

    // ==================== Private helpers ====================

    private void BeginApiNavigation(Guid navigationId)
    {
        _pendingApiNavigation = true;
        _pendingApiNavigationId = navigationId;
    }

    private void ExecuteOrQueue(Action action)
    {
        if (_detached || Volatile.Read(ref _teardownStarted) == 1)
        {
            return;
        }

        if (_webView is not null)
        {
            RunOnUiThread(action);
        }
        else
        {
            lock (_pendingOpsLock)
            {
                if (_detached || Volatile.Read(ref _teardownStarted) == 1)
                    return;
                _pendingOps.Enqueue(action);
            }
        }
    }

    /// <summary>
    /// Runs an action on the UI thread synchronously. If already on the UI thread, executes directly.
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        if (_uiSyncContext is null || SynchronizationContext.Current == _uiSyncContext)
        {
            action();
        }
        else
        {
            _uiSyncContext.Send(_ => action(), null);
        }
    }

    /// <summary>
    /// Runs a function on the UI thread and returns its result. If already on the UI thread, executes directly.
    /// </summary>
    private T RunOnUiThread<T>(Func<T> func)
    {
        if (_uiSyncContext is null || SynchronizationContext.Current == _uiSyncContext)
        {
            return func();
        }

        T result = default!;
        _uiSyncContext.Send(_ => result = func(), null);
        return result;
    }

    /// <summary>
    /// Runs an async function on the UI thread and returns a Task for its result.
    /// </summary>
    private Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> func)
    {
        if (_uiSyncContext is null || SynchronizationContext.Current == _uiSyncContext)
        {
            return func();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiSyncContext.Post(_ =>
        {
            func().ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception!.InnerExceptions);
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(t.Result);
            }, TaskScheduler.Default);
        }, null);
        return tcs.Task;
    }

    private void RaiseNavigationCompleted(Guid navigationId, Uri requestUri, NavigationCompletedStatus status, Exception? error)
        => SafeRaise(() => NavigationCompleted?.Invoke(this, new NavigationCompletedEventArgs(navigationId, requestUri, status, error)));

    private static void SafeRaise(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Keep platform callbacks safe.
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Adapter must be initialized before use.");
        }
    }

    private void ThrowIfNotAttached()
    {
        ThrowIfNotInitialized();

        ObjectDisposedException.ThrowIf(_detached, nameof(WindowsWebViewAdapter));

        if (!_attached)
        {
            throw new InvalidOperationException("Adapter must be attached before use.");
        }
    }

    private void ThrowIfNotAttachedForCookies()
    {
        ObjectDisposedException.ThrowIf(_detached, nameof(WindowsWebViewAdapter));

        if (!_attached || _webView is null)
        {
            throw new InvalidOperationException("Adapter is not attached.");
        }
    }

    private void UpdateControllerBounds()
    {
        if (_controller is null || _parentHwnd == IntPtr.Zero)
        {
            return;
        }

        if (GetClientRect(_parentHwnd, out var rect))
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    // ==================== Parent window subclass for resize ====================

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private const uint WM_SIZE = 0x0005;
    private const int GWLP_WNDPROC = -4;

    private void SubclassParentWindow()
    {
        if (_parentHwnd == IntPtr.Zero) return;

        _wndProcDelegate = WndProc;
        var newWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _originalWndProc = SetWindowLongPtr(_parentHwnd, GWLP_WNDPROC, newWndProc);
    }

    private void RestoreParentWindowProc()
    {
        if (_originalWndProc != IntPtr.Zero && _parentHwnd != IntPtr.Zero)
        {
            SetWindowLongPtr(_parentHwnd, GWLP_WNDPROC, _originalWndProc);
            _originalWndProc = IntPtr.Zero;
        }

        _wndProcDelegate = null;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_SIZE)
        {
            UpdateControllerBounds();
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    // ==================== Win32 interop ====================

    // ==================== Command execution ====================

    public void ExecuteCommand(WebViewCommand command)
    {
        if (_webView is null) return;
        var jsCommand = command switch
        {
            WebViewCommand.Copy => "document.execCommand('copy')",
            WebViewCommand.Cut => "document.execCommand('cut')",
            WebViewCommand.Paste => "document.execCommand('paste')",
            WebViewCommand.SelectAll => "document.execCommand('selectAll')",
            WebViewCommand.Undo => "document.execCommand('undo')",
            WebViewCommand.Redo => "document.execCommand('redo')",
            _ => null
        };
        if (jsCommand is not null)
            _ = _webView.ExecuteScriptAsync(jsCommand);
    }

    // ==================== Screenshot capture ====================

    public async Task<byte[]> CaptureScreenshotAsync()
    {
        ThrowIfNotAttached();
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        using var stream = new MemoryStream();
        await _webView.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return stream.ToArray();
    }

    // ==================== PDF printing ====================

    public async Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
    {
        ThrowIfNotAttached();
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        var settings = _webView.Environment.CreatePrintSettings();
        if (options is not null)
        {
            settings.Orientation = options.Landscape
                ? CoreWebView2PrintOrientation.Landscape
                : CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = options.PageWidth;
            settings.PageHeight = options.PageHeight;
            settings.MarginTop = options.MarginTop;
            settings.MarginBottom = options.MarginBottom;
            settings.MarginLeft = options.MarginLeft;
            settings.MarginRight = options.MarginRight;
            settings.ScaleFactor = options.Scale;
            settings.ShouldPrintBackgrounds = options.PrintBackground;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"webview_print_{Guid.NewGuid():N}.pdf");
        try
        {
            await _webView.PrintToPdfAsync(tempPath, settings);
            return await File.ReadAllBytesAsync(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig]
        int DragEnter([MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, long pt, ref uint pdwEffect);

        [PreserveSig]
        int DragOver(uint grfKeyState, long pt, ref uint pdwEffect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, long pt, ref uint pdwEffect);
    }

    [ComVisible(true)]
    private sealed class DropTargetImpl : IDropTarget
    {
        private readonly WindowsWebViewAdapter _adapter;

        public DropTargetImpl(WindowsWebViewAdapter adapter) => _adapter = adapter;

        public int DragEnter(object pDataObj, uint grfKeyState, long pt, ref uint pdwEffect)
        {
            var payload = ExtractPayload(pDataObj);
            var (x, y) = PointFromLParam(pt);
            var args = new DragEventArgs { Payload = payload, AllowedEffects = DragDropEffects.Copy, Effect = DragDropEffects.Copy, X = x, Y = y };
            _adapter.DragEntered?.Invoke(_adapter, args);
            pdwEffect = (uint)args.Effect;
            return 0; // S_OK
        }

        public int DragOver(uint grfKeyState, long pt, ref uint pdwEffect)
        {
            var (x, y) = PointFromLParam(pt);
            var args = new DragEventArgs { Payload = new DragDropPayload(), AllowedEffects = DragDropEffects.Copy, Effect = DragDropEffects.Copy, X = x, Y = y };
            _adapter.DragOver?.Invoke(_adapter, args);
            pdwEffect = (uint)args.Effect;
            return 0;
        }

        public int DragLeave()
        {
            _adapter.DragLeft?.Invoke(_adapter, EventArgs.Empty);
            return 0;
        }

        public int Drop(object pDataObj, uint grfKeyState, long pt, ref uint pdwEffect)
        {
            var payload = ExtractPayload(pDataObj);
            var (x, y) = PointFromLParam(pt);
            var args = new DropEventArgs { Payload = payload, Effect = DragDropEffects.Copy, X = x, Y = y };
            _adapter.DropCompleted?.Invoke(_adapter, args);
            pdwEffect = (uint)DragDropEffects.Copy;
            return 0;
        }

        private static (double x, double y) PointFromLParam(long pt)
        {
            // POINTL: x = low 32 bits, y = high 32 bits
            int x = (int)(pt & 0xFFFFFFFF);
            int y = (int)((pt >> 32) & 0xFFFFFFFF);
            return (x, y);
        }

        private static DragDropPayload ExtractPayload(object pDataObj)
        {
            // IDataObject extraction would go here with CF_HDROP for files,
            // CF_UNICODETEXT for text, CF_HTML for HTML.
            // For now, return empty payload — full extraction requires
            // IDataObject COM interop which will be implemented in the next iteration.
            return new DragDropPayload();
        }
    }

    private void RegisterDropTarget()
    {
        if (_parentHwnd == IntPtr.Zero) return;
        _dropTarget = new DropTargetImpl(this);
        _ = OleInitialize(IntPtr.Zero);
        _ = RegisterDragDrop(_parentHwnd, _dropTarget);
    }

    private void UnregisterDropTarget()
    {
        if (_parentHwnd != IntPtr.Zero)
        {
            _ = RevokeDragDrop(_parentHwnd);
        }
        _dropTarget = null;
    }

    // ==================== IFindInPageAdapter ====================

    public async Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
    {
        ThrowIfNotAttached();
        if (_webView is null)
            throw new InvalidOperationException("WebView is not initialized.");

        var caseSensitive = options?.CaseSensitive ?? false;
        var forward = options?.Forward ?? true;

        // WebView2 has built-in find API via ICoreWebView2Find (Edge 131+).
        // Fallback to JavaScript-based approach for broad compatibility.
        var escapedText = text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");
        var flags = caseSensitive ? "g" : "gi";
        var countScript = $"(function(){{var re=new RegExp('{escapedText}'.replace(/[.*+?^${{}}()|[\\]\\\\]/g,'\\\\$&'),'{flags}'),"
            + $"m=document.body.innerText.match(re);return m?m.length:0;}})()";

        var countResult = await _webView.ExecuteScriptAsync(countScript);
        var totalMatches = int.TryParse(countResult, out var c) ? c : 0;

        var forwardStr = forward ? "false" : "true"; // window.find 3rd param is "backwards"
        var csStr = caseSensitive ? "true" : "false";
        var findScript = $"window.find('{escapedText}',{csStr},{forwardStr},true,false,true,false)";
        var findResult = await _webView.ExecuteScriptAsync(findScript);
        var activeIndex = findResult == "true" ? 0 : -1;

        return new FindInPageEventArgs
        {
            ActiveMatchIndex = activeIndex,
            TotalMatches = totalMatches
        };
    }

    public void StopFind(bool clearHighlights = true)
    {
        if (_webView is null || _detached) return;
        _ = _webView.ExecuteScriptAsync("window.getSelection().removeAllRanges()");
    }

    // ==================== IZoomAdapter ====================

    public event EventHandler<double>? ZoomFactorChanged;

    public double ZoomFactor
    {
        get => _controller?.ZoomFactor ?? 1.0;
        set
        {
            if (_controller is null || _detached) return;
            _controller.ZoomFactor = value;
            ZoomFactorChanged?.Invoke(this, value);
        }
    }

    // ==================== IPreloadScriptAdapter ====================

    public string AddPreloadScript(string javaScript)
    {
        // Legacy sync path kept for compatibility with obsolete sync API.
        // Runtime async path should use AddPreloadScriptAsync to avoid blocking UI.
        return AddPreloadScriptAsync(javaScript).GetAwaiter().GetResult();
    }

    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return RunOnUiThreadAsync(async () =>
        {
            ThrowIfNotAttached();
            if (_webView is null)
                throw new InvalidOperationException("WebView is not initialized.");
            return await _webView.AddScriptToExecuteOnDocumentCreatedAsync(javaScript).ConfigureAwait(false);
        });
    }

    public void RemovePreloadScript(string scriptId)
    {
        if (_webView is null || _detached) return;
        _webView.RemoveScriptToExecuteOnDocumentCreated(scriptId);
    }

    public Task RemovePreloadScriptAsync(string scriptId)
    {
        RunOnUiThread(() => RemovePreloadScript(scriptId));
        return Task.CompletedTask;
    }

    // ==================== IContextMenuAdapter ====================

    // WebView2 context-menu interception is not wired in this adapter yet.
    // Keep no-op accessors to satisfy IContextMenuAdapter without triggering unused-event warnings.
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add { }
        remove { }
    }

    // ==================== IDevToolsAdapter ====================

    public void OpenDevTools()
    {
        if (_webView is not null)
            _webView.OpenDevToolsWindow();
    }

    public void CloseDevTools()
    {
        // WebView2 does not have a direct "close DevTools" API.
        // Closing is only possible by the user or by reloading settings.
        // This is a platform limitation — no-op.
    }

    public bool IsDevToolsOpen => false; // WebView2 does not expose this state
}
