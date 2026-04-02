using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Core runtime implementation of <see cref="IWebView"/> over a platform adapter.
/// </summary>
public sealed class WebViewCore : ISpaHostingWebView, IWebViewAdapterHost, IDisposable
{
    private static readonly Uri AboutBlank = new("about:blank");

    private readonly IWebViewAdapter _adapter;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger<WebViewCore> _logger;
    private readonly IWebViewEnvironmentOptions _environmentOptions;
    private readonly object _operationQueueLock = new();
    private Task _operationQueueTail = Task.CompletedTask;
    private long _operationSequence;

    /// <summary>
    /// Creates a new <see cref="IWebView"/> using the default platform adapter for the current OS.
    /// This is the recommended entry-point; callers never need to reference the internal adapter types.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static IWebView CreateDefault(IWebViewDispatcher dispatcher)
        => CreateDefault(dispatcher, NullLogger<WebViewCore>.Instance);

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static IWebView CreateDefault(IWebViewDispatcher dispatcher, ILogger<WebViewCore> logger)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);
        return new WebViewCore(WebViewAdapterFactory.CreateDefaultAdapter(), dispatcher, logger);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static WebViewCore CreateForControl(
        IWebViewDispatcher dispatcher,
        ILogger<WebViewCore>? logger = null,
        IWebViewEnvironmentOptions? environmentOptions = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return new WebViewCore(
            WebViewAdapterFactory.CreateDefaultAdapter(),
            dispatcher,
            logger ?? NullLogger<WebViewCore>.Instance,
            environmentOptions);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal void Attach(INativeHandle parentHandle)
    {
        _lifecycleState = WebViewLifecycleState.Attaching;
        _logger.LogDebug("Attach: parentHandle.HandleDescriptor={Descriptor}", parentHandle.HandleDescriptor);
        _adapter.Attach(parentHandle);
        _lifecycleState = WebViewLifecycleState.Ready;
        _logger.LogDebug("Attach: completed");

        // Raise AdapterCreated after successful attach, before any pending navigation.
        var handle = TryGetWebViewHandle();
        _logger.LogDebug("AdapterCreated: raising with handle={HasHandle}", handle is not null);
        AdapterCreated?.Invoke(this, new AdapterCreatedEventArgs(handle));
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal void Detach()
    {
        _lifecycleState = WebViewLifecycleState.Detaching;
        _logger.LogDebug("Detach: begin");
        RaiseAdapterDestroyedOnce();
        _adapter.Detach();
        _logger.LogDebug("Detach: completed");
    }

    // Volatile: checked off-UI-thread in adapter callbacks before dispatching.
    private volatile bool _disposed;

    // Guards at-most-once firing of AdapterDestroyed.
    private bool _adapterDestroyed;

    // Only accessed on the UI thread (all paths go through _dispatcher).
    private NavigationOperation? _activeNavigation;
    private Uri _source;
    private volatile WebViewLifecycleState _lifecycleState = WebViewLifecycleState.Created;

    private readonly ICookieManager? _cookieManager;
    private readonly ICommandManager? _commandManager;
    private readonly IScreenshotAdapter? _screenshotAdapter;
    private readonly IPrintAdapter? _printAdapter;
    private readonly IFindInPageAdapter? _findInPageAdapter;
    private readonly IZoomAdapter? _zoomAdapter;
    private readonly IPreloadScriptAdapter? _preloadScriptAdapter;
    private readonly IAsyncPreloadScriptAdapter? _asyncPreloadScriptAdapter;
    private readonly IContextMenuAdapter? _contextMenuAdapter;
    private readonly IDragDropAdapter? _dragDropAdapter;

    /// <summary>Whether the current adapter supports drag-and-drop.</summary>
    internal bool HasDragDropSupport => _dragDropAdapter is not null;

    private bool _webMessageBridgeEnabled;
    private IWebMessagePolicy? _webMessagePolicy;
    private IWebMessageDropDiagnosticsSink? _webMessageDropDiagnosticsSink;
    private IFuloraDiagnosticsSink? _fuloraDiagnosticsSink;
    private WebViewRpcService? _rpcService;
    private RuntimeBridgeService? _bridgeService;
    private IBridgeTracer? _bridgeTracer;
    private SpaHostingService? _spaHostingService;

    internal WebViewCore(IWebViewAdapter adapter, IWebViewDispatcher dispatcher)
        : this(adapter, dispatcher, NullLogger<WebViewCore>.Instance)
    {
    }

    internal WebViewCore(
        IWebViewAdapter adapter,
        IWebViewDispatcher dispatcher,
        ILogger<WebViewCore> logger,
        IWebViewEnvironmentOptions? environmentOptions = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? NullLogger<WebViewCore>.Instance;
        _environmentOptions = environmentOptions ?? WebViewEnvironment.Options;

        _source = AboutBlank;
        ChannelId = Guid.NewGuid();

        _logger.LogDebug("WebViewCore created: channelId={ChannelId}, adapter={AdapterType}",
            ChannelId, adapter.GetType().FullName);

        _adapter.Initialize(this);
        _logger.LogDebug("Adapter initialized");

        // Apply global environment options if adapter supports them.
        if (_adapter is IWebViewAdapterOptions adapterOptions)
        {
            var envOptions = _environmentOptions;
            adapterOptions.ApplyEnvironmentOptions(envOptions);
            _logger.LogDebug("Environment options applied: DevTools={DevTools}, Ephemeral={Ephemeral}, UA={UA}",
                envOptions.EnableDevTools, envOptions.UseEphemeralSession, envOptions.CustomUserAgent ?? "(default)");
        }

        _cookieManager = adapter is ICookieAdapter cookieAdapter
            ? new RuntimeCookieManager(cookieAdapter, this, _logger)
            : null;
        _logger.LogDebug("Cookie support: {Supported}", _cookieManager is not null);

        // Register custom schemes if adapter supports it.
        if (_adapter is ICustomSchemeAdapter customSchemeAdapter)
        {
            var schemes = _environmentOptions.CustomSchemes;
            if (schemes.Count > 0)
            {
                customSchemeAdapter.RegisterCustomSchemes(schemes);
                _logger.LogDebug("Custom schemes registered: {Count}", schemes.Count);
            }
        }

        // Subscribe to download events if adapter supports it.
        if (_adapter is IDownloadAdapter downloadAdapter)
        {
            downloadAdapter.DownloadRequested += OnAdapterDownloadRequested;
            _logger.LogDebug("Download support: enabled");
        }

        // Subscribe to permission events if adapter supports it.
        if (_adapter is IPermissionAdapter permissionAdapter)
        {
            permissionAdapter.PermissionRequested += OnAdapterPermissionRequested;
            _logger.LogDebug("Permission support: enabled");
        }

        // Detect command support.
        _commandManager = _adapter is ICommandAdapter commandAdapter
            ? new RuntimeCommandManager(commandAdapter, this)
            : null;
        _logger.LogDebug("Command support: {Supported}", _commandManager is not null);

        // Detect screenshot support.
        _screenshotAdapter = _adapter as IScreenshotAdapter;
        _logger.LogDebug("Screenshot support: {Supported}", _screenshotAdapter is not null);

        // Detect print support.
        _printAdapter = _adapter as IPrintAdapter;
        _logger.LogDebug("Print support: {Supported}", _printAdapter is not null);

        // Detect find-in-page support.
        _findInPageAdapter = _adapter as IFindInPageAdapter;
        _logger.LogDebug("Find-in-page support: {Supported}", _findInPageAdapter is not null);

        // Detect zoom support.
        _zoomAdapter = _adapter as IZoomAdapter;
        if (_zoomAdapter is not null)
        {
            _zoomAdapter.ZoomFactorChanged += OnAdapterZoomFactorChanged;
        }
        _logger.LogDebug("Zoom support: {Supported}", _zoomAdapter is not null);

        // Detect preload script support and apply global scripts.
        _preloadScriptAdapter = _adapter as IPreloadScriptAdapter;
        _asyncPreloadScriptAdapter = _adapter as IAsyncPreloadScriptAdapter;
        if (_preloadScriptAdapter is not null)
        {
            var globalScripts = _environmentOptions.PreloadScripts;
            foreach (var script in globalScripts)
            {
                _preloadScriptAdapter.AddPreloadScript(script);
            }
            if (globalScripts.Count > 0)
                _logger.LogDebug("Global preload scripts applied: {Count}", globalScripts.Count);
        }
        _logger.LogDebug("Preload script support: {Supported}", _preloadScriptAdapter is not null);

        // Subscribe to context menu events if adapter supports it.
        _contextMenuAdapter = _adapter as IContextMenuAdapter;
        if (_contextMenuAdapter is not null)
        {
            _contextMenuAdapter.ContextMenuRequested += OnAdapterContextMenuRequested;
        }
        _logger.LogDebug("Context menu support: {Supported}", _contextMenuAdapter is not null);

        _dragDropAdapter = _adapter as IDragDropAdapter;
        if (_dragDropAdapter is not null)
        {
            _dragDropAdapter.DragEntered += OnAdapterDragEntered;
            _dragDropAdapter.DragOver += OnAdapterDragOver;
            _dragDropAdapter.DragLeft += OnAdapterDragLeft;
            _dragDropAdapter.DropCompleted += OnAdapterDropCompleted;
        }
        _logger.LogDebug("Drag-drop support: {Supported}", _dragDropAdapter is not null);

        _adapter.NavigationCompleted += OnAdapterNavigationCompleted;
        _adapter.NewWindowRequested += OnAdapterNewWindowRequested;
        _adapter.WebMessageReceived += OnAdapterWebMessageReceived;
        _adapter.WebResourceRequested += OnAdapterWebResourceRequested;
        _adapter.EnvironmentRequested += OnAdapterEnvironmentRequested;
    }

    /// <inheritdoc />
    public Uri Source
    {
        get => _source;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfDisposed();
            ThrowIfNotOnUiThread(nameof(Source));

            _logger.LogDebug("Source set: {Uri}", value);
            SetSourceInternal(value);

            // Source is a sync API surface; we still start navigation to keep semantics consistent.
            var navigationTask = StartNavigationCoreAsync(
                requestUri: value,
                adapterInvoke: navigationId => _adapter.NavigateAsync(navigationId, value),
                updateSource: false);
            ObserveBackgroundTask(navigationTask, nameof(Source));
        }
    }

    /// <inheritdoc />
    public bool CanGoBack => _adapter.CanGoBack;

    /// <inheritdoc />
    public bool CanGoForward => _adapter.CanGoForward;

    /// <inheritdoc />
    public bool IsLoading => _activeNavigation is not null;

    /// <inheritdoc />
    public Guid ChannelId { get; }

    /// <inheritdoc />
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted;
    /// <inheritdoc />
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    /// <inheritdoc />
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    /// <inheritdoc />
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    /// <inheritdoc />
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    /// <inheritdoc />
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
    /// <inheritdoc />
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    /// <inheritdoc />
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    /// <inheritdoc />
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated;
    /// <inheritdoc />
    public event EventHandler? AdapterDestroyed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Dispose: begin");

        // Raise AdapterDestroyed if not already raised during Detach().
        RaiseAdapterDestroyedOnce();

        _disposed = true;
        _lifecycleState = WebViewLifecycleState.Disposed;

        _adapter.NavigationCompleted -= OnAdapterNavigationCompleted;
        _adapter.NewWindowRequested -= OnAdapterNewWindowRequested;
        _adapter.WebMessageReceived -= OnAdapterWebMessageReceived;
        _adapter.WebResourceRequested -= OnAdapterWebResourceRequested;
        _adapter.EnvironmentRequested -= OnAdapterEnvironmentRequested;

        if (_adapter is IDownloadAdapter downloadAdapter)
            downloadAdapter.DownloadRequested -= OnAdapterDownloadRequested;
        if (_adapter is IPermissionAdapter permissionAdapter)
            permissionAdapter.PermissionRequested -= OnAdapterPermissionRequested;
        if (_zoomAdapter is not null)
            _zoomAdapter.ZoomFactorChanged -= OnAdapterZoomFactorChanged;

        if (_contextMenuAdapter is not null)
            _contextMenuAdapter.ContextMenuRequested -= OnAdapterContextMenuRequested;

        if (_dragDropAdapter is not null)
        {
            _dragDropAdapter.DragEntered -= OnAdapterDragEntered;
            _dragDropAdapter.DragOver -= OnAdapterDragOver;
            _dragDropAdapter.DragLeft -= OnAdapterDragLeft;
            _dragDropAdapter.DropCompleted -= OnAdapterDropCompleted;
        }

        if (_activeNavigation is not null)
        {
            _logger.LogDebug("Dispose: faulting active navigation id={NavigationId}", _activeNavigation.NavigationId);
            // After disposal, async APIs must not hang. No events must be raised.
            _activeNavigation.TrySetFault(new ObjectDisposedException(nameof(WebViewCore)));
            _activeNavigation = null;
        }

        _bridgeService?.Dispose();
        _bridgeService = null;

        _spaHostingService?.Dispose();
        _spaHostingService = null;

        _logger.LogDebug("Dispose: completed");
    }

    /// <inheritdoc />
    public Task NavigateAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _logger.LogDebug("NavigateAsync: {Uri}", uri);

        return EnqueueOperationAsync(nameof(NavigateAsync), () => StartNavigationRequestCoreAsync(
            requestUri: uri,
            adapterInvoke: navigationId => _adapter.NavigateAsync(navigationId, uri))).Unwrap();
    }

    /// <inheritdoc />
    public Task NavigateToStringAsync(string html)
        => NavigateToStringAsync(html, baseUrl: null);

    /// <inheritdoc />
    public Task NavigateToStringAsync(string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        var requestUri = baseUrl ?? AboutBlank;
        _logger.LogDebug("NavigateToStringAsync: html length={Length}, baseUrl={BaseUrl}", html.Length, baseUrl);

        return EnqueueOperationAsync(nameof(NavigateToStringAsync), () => StartNavigationRequestCoreAsync(
            requestUri: requestUri,
            adapterInvoke: navigationId => _adapter.NavigateToStringAsync(navigationId, html, baseUrl))).Unwrap();
    }

    /// <inheritdoc />
    public Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _logger.LogDebug("InvokeScriptAsync: script length={Length}", script.Length);

        if (_disposed)
        {
            return Task.FromException<string?>(new ObjectDisposedException(nameof(WebViewCore)));
        }

        return EnqueueOperationAsync(nameof(InvokeScriptAsync), () => InvokeScriptOnUiThreadAsync(script));

        async Task<string?> InvokeScriptOnUiThreadAsync(string s)
        {
            ThrowIfDisposed();

            try
            {
                var result = await _adapter.InvokeScriptAsync(s).ConfigureAwait(false);
                _logger.LogDebug("InvokeScriptAsync: result length={Length}", result?.Length ?? 0);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "InvokeScriptAsync: failed");
                throw new WebViewScriptException("Script execution failed.", ex);
            }
        }
    }

    /// <inheritdoc />
    public Task<bool> GoBackAsync()
        => EnqueueOperationAsync(nameof(GoBackAsync), () => Task.FromResult(GoBackCore()));

    private bool GoBackCore()
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(GoBackAsync));

        if (!_adapter.CanGoBack)
        {
            _logger.LogDebug("GoBack: no history, skipped");
            return false;
        }

        var navigationId = StartCommandNavigation(requestUri: Source);
        if (navigationId == Guid.Empty)
        {
            _logger.LogDebug("GoBack: canceled by NavigationStarted handler");
            return false;
        }

        var accepted = _adapter.GoBack(navigationId);
        if (!accepted)
        {
            _logger.LogDebug("GoBack: adapter rejected, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return false;
        }

        _logger.LogDebug("GoBack: started, id={NavigationId}", navigationId);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> GoForwardAsync()
        => EnqueueOperationAsync(nameof(GoForwardAsync), () => Task.FromResult(GoForwardCore()));

    private bool GoForwardCore()
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(GoForwardAsync));

        if (!_adapter.CanGoForward)
        {
            _logger.LogDebug("GoForward: no forward history, skipped");
            return false;
        }

        var navigationId = StartCommandNavigation(requestUri: Source);
        if (navigationId == Guid.Empty)
        {
            _logger.LogDebug("GoForward: canceled by NavigationStarted handler");
            return false;
        }

        var accepted = _adapter.GoForward(navigationId);
        if (!accepted)
        {
            _logger.LogDebug("GoForward: adapter rejected, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return false;
        }

        _logger.LogDebug("GoForward: started, id={NavigationId}", navigationId);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> RefreshAsync()
        => EnqueueOperationAsync(nameof(RefreshAsync), () => Task.FromResult(RefreshCore()));

    private bool RefreshCore()
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(RefreshAsync));

        var navigationId = StartCommandNavigation(requestUri: Source);
        if (navigationId == Guid.Empty)
        {
            _logger.LogDebug("Refresh: canceled by NavigationStarted handler");
            return false;
        }

        var accepted = _adapter.Refresh(navigationId);
        if (!accepted)
        {
            _logger.LogDebug("Refresh: adapter rejected, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return false;
        }

        _logger.LogDebug("Refresh: started, id={NavigationId}", navigationId);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> StopAsync()
        => EnqueueOperationAsync(nameof(StopAsync), () => Task.FromResult(StopCore()));

    private bool StopCore()
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(StopAsync));

        if (_activeNavigation is null)
        {
            _logger.LogDebug("Stop: no active navigation");
            return false;
        }

        _logger.LogDebug("Stop: canceling active navigation id={NavigationId}", _activeNavigation.NavigationId);
        _adapter.Stop();
        CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
        return true;
    }

    ValueTask<NativeNavigationStartingDecision> IWebViewAdapterHost.OnNativeNavigationStartingAsync(NativeNavigationStartingInfo info)
    {
        _logger.LogDebug("OnNativeNavigationStarting: correlationId={CorrelationId}, uri={Uri}, isMainFrame={IsMainFrame}",
            info.CorrelationId, info.RequestUri, info.IsMainFrame);

        if (_disposed)
        {
            _logger.LogDebug("OnNativeNavigationStarting: disposed, denying");
            return ValueTask.FromResult(new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: Guid.Empty));
        }

        if (_dispatcher.CheckAccess())
        {
            return ValueTask.FromResult(OnNativeNavigationStartingOnUiThread(info));
        }

        return new ValueTask<NativeNavigationStartingDecision>(
            _dispatcher.InvokeAsync(() => OnNativeNavigationStartingOnUiThread(info)));
    }

    private NativeNavigationStartingDecision OnNativeNavigationStartingOnUiThread(NativeNavigationStartingInfo info)
    {
        if (_disposed)
        {
            return new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: Guid.Empty);
        }

        ThrowIfNotOnUiThread(nameof(IWebViewAdapterHost.OnNativeNavigationStartingAsync));

        // Sub-frame navigations are not part of the v1 contract surface.
        if (!info.IsMainFrame)
        {
            _logger.LogDebug("OnNativeNavigationStarting: sub-frame, auto-allow");
            return new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: Guid.Empty);
        }

        var requestUri = info.RequestUri.AbsoluteUri != AboutBlank.AbsoluteUri ? info.RequestUri : AboutBlank;

        // Redirects / subsequent navigation actions within the same correlation id stay within one NavigationId.
        if (TryHandleNavigationRedirect(info, requestUri, out var redirectDecision))
            return redirectDecision;

        // New native navigation supersedes any active navigation.
        HandleNavigationSupersession();

        SetSourceInternal(requestUri);

        var navigationId = Guid.NewGuid();
        _activeNavigation = new NavigationOperation(navigationId, correlationId: info.CorrelationId, requestUri);

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (native): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        NavigationStarted?.Invoke(this, startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: navigationId);
        }

        _logger.LogDebug("OnNativeNavigationStarting: allowed, id={NavigationId}", navigationId);
        return new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: navigationId);
    }

    private bool TryHandleNavigationRedirect(
        NativeNavigationStartingInfo info,
        Uri requestUri,
        out NativeNavigationStartingDecision decision)
    {
        if (_activeNavigation is null || _activeNavigation.CorrelationId != info.CorrelationId)
        {
            decision = default;
            return false;
        }

        if (_activeNavigation.RequestUri.AbsoluteUri == requestUri.AbsoluteUri)
        {
            _logger.LogDebug("OnNativeNavigationStarting: same-URL redirect, id={NavigationId}", _activeNavigation.NavigationId);
            decision = new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: _activeNavigation.NavigationId);
            return true;
        }

        _activeNavigation.UpdateRequestUri(requestUri);
        SetSourceInternal(requestUri);

        var redirectArgs = new NavigationStartingEventArgs(_activeNavigation.NavigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (redirect): id={NavigationId}, uri={Uri}", _activeNavigation.NavigationId, requestUri);
        NavigationStarted?.Invoke(this, redirectArgs);

        if (redirectArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: redirect canceled by handler, id={NavigationId}", _activeNavigation.NavigationId);
            var activeNavigationId = _activeNavigation.NavigationId;
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            decision = new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: activeNavigationId);
            return true;
        }

        decision = new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: _activeNavigation.NavigationId);
        return true;
    }

    private void HandleNavigationSupersession()
    {
        if (_activeNavigation is null)
            return;

        _logger.LogDebug("OnNativeNavigationStarting: superseding active navigation id={NavigationId}", _activeNavigation.NavigationId);
        CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
    }

    /// <inheritdoc />
    public ICookieManager? TryGetCookieManager() => _cookieManager;

    /// <inheritdoc />
    public ICommandManager? TryGetCommandManager() => _commandManager;

    /// <inheritdoc />
    public IWebViewRpcService? Rpc => _rpcService;

    // ==================== DevTools ====================

    /// <inheritdoc />
    public Task OpenDevToolsAsync()
    {
        return EnqueueOperationAsync(nameof(OpenDevToolsAsync), () =>
        {
            ThrowIfDisposed();
            if (_adapter is IDevToolsAdapter devTools)
                devTools.OpenDevTools();
            else
                _logger.LogDebug("DevTools: adapter does not support runtime toggle");

            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public Task CloseDevToolsAsync()
    {
        return EnqueueOperationAsync(nameof(CloseDevToolsAsync), () =>
        {
            ThrowIfDisposed();
            if (_adapter is IDevToolsAdapter devTools)
                devTools.CloseDevTools();

            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public Task<bool> IsDevToolsOpenAsync()
    {
        return EnqueueOperationAsync(nameof(IsDevToolsOpenAsync), () =>
        {
            ThrowIfDisposed();
            return Task.FromResult(_adapter is IDevToolsAdapter devTools && devTools.IsDevToolsOpen);
        });
    }

    // ==================== Bridge ====================

    /// <inheritdoc />
    public IBridgeTracer? BridgeTracer
    {
        get => _bridgeTracer;
        set
        {
            if (_bridgeService is not null)
            {
                _logger.LogWarning("BridgeTracer set after Bridge was already created — change ignored.");
                return;
            }
            _bridgeTracer = value;
        }
    }

    /// <inheritdoc />
    public IBridgeService Bridge
    {
        get
        {
            ThrowIfDisposed();

            if (_bridgeService is not null)
                return _bridgeService;

            // Auto-enable bridge with defaults if needed.
            if (!_webMessageBridgeEnabled)
            {
                EnableWebMessageBridge(new WebMessageBridgeOptions
                {
                    EnableDevToolsDiagnostics = _environmentOptions.EnableDevTools
                });
            }

            _bridgeService = new RuntimeBridgeService(
                _rpcService!,
                script => InvokeScriptAsync(script),
                _logger,
                enableDevTools: _environmentOptions.EnableDevTools,
                tracer: _bridgeTracer);

            _logger.LogDebug("Bridge: auto-created RuntimeBridgeService");
            return _bridgeService;
        }
    }

    /// <inheritdoc />
    public Task<byte[]> CaptureScreenshotAsync()
    {
        return EnqueueOperationAsync(nameof(CaptureScreenshotAsync), () =>
        {
            ThrowIfDisposed();
            if (_screenshotAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support screenshot capture.");
            return _screenshotAdapter.CaptureScreenshotAsync();
        });
    }

    /// <inheritdoc />
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
    {
        return EnqueueOperationAsync(nameof(PrintToPdfAsync), () =>
        {
            ThrowIfDisposed();
            if (_printAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support PDF printing.");
            return _printAdapter.PrintToPdfAsync(options);
        });
    }

    // ==================== Zoom ====================

    private const double MinZoom = 0.25;
    private const double MaxZoom = 5.0;

    /// <summary>Raised when the zoom factor changes.</summary>
    public event EventHandler<double>? ZoomFactorChanged;

    /// <summary>
    /// Gets or sets the zoom factor (1.0 = 100%). Clamped to [0.25, 5.0].
    /// Returns 1.0 if the adapter does not support zoom.
    /// </summary>
    public Task<double> GetZoomFactorAsync()
    {
        return EnqueueOperationAsync(nameof(GetZoomFactorAsync), () =>
        {
            ThrowIfDisposed();
            return Task.FromResult(_zoomAdapter?.ZoomFactor ?? 1.0);
        });
    }

    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor)
    {
        return EnqueueOperationAsync(nameof(SetZoomFactorAsync), () =>
        {
            ThrowIfDisposed();
            if (_zoomAdapter is null) return Task.CompletedTask; // no-op without adapter
            _zoomAdapter.ZoomFactor = Math.Clamp(zoomFactor, MinZoom, MaxZoom);
            return Task.CompletedTask;
        });
    }

    private void OnAdapterZoomFactorChanged(object? sender, double newZoom)
    {
        if (_disposed) return;
        _ = _dispatcher.InvokeAsync(() => ZoomFactorChanged?.Invoke(this, newZoom));
    }

    /// <summary>Raised when the user triggers a context menu (right-click, long-press).</summary>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

    /// <summary>Raised when a drag operation enters the WebView bounds.</summary>
    public event EventHandler<DragEventArgs>? DragEntered;
    /// <summary>Raised when a drag operation moves over the WebView.</summary>
    public event EventHandler<DragEventArgs>? DragOver;
    /// <summary>Raised when a drag operation leaves the WebView bounds.</summary>
    public event EventHandler<EventArgs>? DragLeft;
    /// <summary>Raised when a drop is completed on the WebView.</summary>
    public event EventHandler<DropEventArgs>? DropCompleted;

    private void OnAdapterContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        if (_disposed) return;
        _ = _dispatcher.InvokeAsync(() => ContextMenuRequested?.Invoke(this, e));
    }

    private void OnAdapterDragEntered(object? sender, DragEventArgs e) => DragEntered?.Invoke(this, e);
    private void OnAdapterDragOver(object? sender, DragEventArgs e) => DragOver?.Invoke(this, e);
    private void OnAdapterDragLeft(object? sender, EventArgs e) => DragLeft?.Invoke(this, e);
    private void OnAdapterDropCompleted(object? sender, DropEventArgs e) => DropCompleted?.Invoke(this, e);

    /// <summary>
    /// Searches the current page for the given text.
    /// </summary>
    /// <param name="text">The search text. Must not be null or empty.</param>
    /// <param name="options">Optional search options (case sensitivity, direction).</param>
    /// <returns>A <see cref="FindInPageEventArgs"/> with match count and active index.</returns>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IFindInPageAdapter"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
    {
        return EnqueueOperationAsync(nameof(FindInPageAsync), () =>
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("Search text must not be null or empty.", nameof(text));
            if (_findInPageAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support find-in-page.");
            return _findInPageAdapter.FindAsync(text, options);
        });
    }

    /// <summary>
    /// Clears find-in-page highlights and resets search state.
    /// </summary>
    /// <param name="clearHighlights">Whether to remove visual highlights. Default: true.</param>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IFindInPageAdapter"/>.</exception>
    public Task StopFindInPageAsync(bool clearHighlights = true)
    {
        return EnqueueOperationAsync(nameof(StopFindInPageAsync), () =>
        {
            ThrowIfDisposed();
            if (_findInPageAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support find-in-page.");
            _findInPageAdapter.StopFind(clearHighlights);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Registers a JavaScript snippet to run at document start on every page load.
    /// </summary>
    /// <param name="javaScript">The script to inject.</param>
    /// <returns>An opaque script ID that can be passed to <see cref="RemovePreloadScriptAsync"/>.</returns>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IPreloadScriptAdapter"/>.</exception>
    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return EnqueueOperationAsync(nameof(AddPreloadScriptAsync), () =>
        {
            ThrowIfDisposed();
            if (_asyncPreloadScriptAdapter is not null)
            {
                return _asyncPreloadScriptAdapter.AddPreloadScriptAsync(javaScript);
            }

            if (_preloadScriptAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support preload scripts.");
            return Task.FromResult(_preloadScriptAdapter.AddPreloadScript(javaScript));
        });
    }

    /// <summary>
    /// Removes a previously registered preload script by its ID.
    /// </summary>
    /// <param name="scriptId">The ID returned by <see cref="AddPreloadScriptAsync"/>.</param>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IPreloadScriptAdapter"/>.</exception>
    public Task RemovePreloadScriptAsync(string scriptId)
    {
        return EnqueueOperationAsync(nameof(RemovePreloadScriptAsync), () =>
        {
            ThrowIfDisposed();
            if (_asyncPreloadScriptAdapter is not null)
            {
                return _asyncPreloadScriptAdapter.RemovePreloadScriptAsync(scriptId);
            }

            if (_preloadScriptAdapter is null)
                throw new NotSupportedException("The current WebView adapter does not support preload scripts.");
            _preloadScriptAdapter.RemovePreloadScript(scriptId);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Asynchronously retrieves the native platform WebView handle.
    /// This is the primary any-thread API surface and always marshals adapter access to the UI thread.
    /// </summary>
    public Task<INativeHandle?> TryGetWebViewHandleAsync()
    {
        if (_adapterDestroyed)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_adapter is not INativeWebViewHandleProvider provider)
        {
            return Task.FromResult<INativeHandle?>(null);
        }

        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(provider.TryGetWebViewHandle());
        }

        return _dispatcher.InvokeAsync(() => provider.TryGetWebViewHandle());
    }

    /// <summary>
    /// Compatibility wrapper around <see cref="TryGetWebViewHandleAsync"/> for synchronous call sites.
    /// Prefer using <see cref="TryGetWebViewHandleAsync"/> to avoid blocking.
    /// </summary>
    public INativeHandle? TryGetWebViewHandle()
    {
        return TryGetWebViewHandleAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Sets the custom User-Agent string at runtime.
    /// Pass <c>null</c> to revert to the platform default.
    /// </summary>
    public void SetCustomUserAgent(string? userAgent)
    {
        ThrowIfDisposed();
        if (_adapter is IWebViewAdapterOptions adapterOptions)
        {
            if (_dispatcher.CheckAccess())
            {
                adapterOptions.SetCustomUserAgent(userAgent);
            }
            else
            {
                var dispatchTask = _dispatcher.InvokeAsync(() => adapterOptions.SetCustomUserAgent(userAgent));
                ObserveBackgroundTask(dispatchTask, nameof(SetCustomUserAgent));
            }

            _logger.LogDebug("CustomUserAgent set to: {UA}", userAgent ?? "(default)");
        }
    }

    /// <inheritdoc />
    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(EnableWebMessageBridge));

        _webMessageBridgeEnabled = true;
        _webMessagePolicy = new DefaultWebMessagePolicy(options.AllowedOrigins, options.ProtocolVersion, ChannelId);
        _webMessageDropDiagnosticsSink = options.DropDiagnosticsSink;
        _fuloraDiagnosticsSink = options.DiagnosticsSink;
        _rpcService ??= new WebViewRpcService(script => InvokeScriptAsync(script), _logger, options.EnableDevToolsDiagnostics);

        // Inject RPC JS stub.
        ObserveBackgroundTask(InvokeScriptAsync(WebViewRpcService.JsStub), $"{nameof(EnableWebMessageBridge)}.{nameof(WebViewRpcService.JsStub)}");

        _logger.LogDebug("WebMessageBridge enabled: originCount={Count}, protocol={Protocol}",
            options.AllowedOrigins?.Count ?? 0, options.ProtocolVersion);
    }

    /// <inheritdoc />
    public void DisableWebMessageBridge()
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread(nameof(DisableWebMessageBridge));

        _webMessageBridgeEnabled = false;
        _webMessagePolicy = null;
        _webMessageDropDiagnosticsSink = null;
        _fuloraDiagnosticsSink = null;
        _rpcService = null;

        _logger.LogDebug("WebMessageBridge disabled");
    }

    /// <summary>
    /// Re-injects the base RPC JS stub and all exposed service stubs when the bridge is enabled.
    /// Called after successful navigation to restore <c>window.agWebView</c> in the new JS context.
    /// </summary>
    internal void ReinjectBridgeStubsIfEnabled()
    {
        if (!_webMessageBridgeEnabled)
            return;

        ObserveBackgroundTask(InvokeScriptAsync(WebViewRpcService.JsStub), "ReinjectBridgeStubs.RpcStub");
        _bridgeService?.ReinjectServiceStubs();

        _logger.LogDebug("Bridge: re-injected JS stubs after navigation");
    }

    // ==================== SPA Hosting ====================

    /// <summary>
    /// Enables SPA hosting. Registers the custom scheme, subscribes to WebResourceRequested,
    /// and optionally auto-enables the bridge.
    /// </summary>
    public void EnableSpaHosting(SpaHostingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();

        if (_spaHostingService is not null)
            throw new InvalidOperationException("SPA hosting is already enabled.");

        _spaHostingService = new SpaHostingService(options, _logger);

        // Register custom scheme with the adapter.
        if (_adapter is ICustomSchemeAdapter customSchemeAdapter)
        {
            customSchemeAdapter.RegisterCustomSchemes([_spaHostingService.GetSchemeRegistration()]);
        }

        // Subscribe to WebResourceRequested to intercept app:// requests.
        WebResourceRequested += OnSpaWebResourceRequested;

        // Auto-enable bridge if requested.
        if (options.AutoInjectBridgeScript && !_webMessageBridgeEnabled)
        {
            EnableWebMessageBridge(new WebMessageBridgeOptions());
        }

        _logger.LogDebug("SPA hosting enabled: scheme={Scheme}, devServer={DevServer}",
            options.Scheme, options.DevServerUrl ?? "(embedded)");
    }

    private void OnSpaWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
    {
        _spaHostingService?.TryHandle(e);
    }

    private Task<object?> EnqueueOperationAsync(string operationType, Func<Task> func)
        => EnqueueOperationAsync<object?>(operationType, async () =>
        {
            await func().ConfigureAwait(false);
            return null;
        });

    private Task<T> EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func)
    {
        if (_disposed)
        {
            return Task.FromException<T>(ClassifyFailure(
                new ObjectDisposedException(nameof(WebViewCore)),
                operationType,
                defaultCategory: WebViewOperationFailureCategory.Disposed));
        }

        if (!IsOperationAcceptedInCurrentState())
        {
            return Task.FromException<T>(ClassifyFailure(
                new InvalidOperationException($"Operation '{operationType}' is not allowed in state '{_lifecycleState}'."),
                operationType,
                defaultCategory: WebViewOperationFailureCategory.NotReady));
        }

        var operationId = Interlocked.Increment(ref _operationSequence);
        var enqueueTs = DateTimeOffset.UtcNow;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_operationQueueLock)
        {
            _operationQueueTail = _operationQueueTail.ContinueWith(
                _ => RunQueuedOperationAsync(operationId, operationType, enqueueTs, func, tcs),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }

        return tcs.Task;
    }

    private bool IsOperationAcceptedInCurrentState()
        => _lifecycleState is WebViewLifecycleState.Created
            or WebViewLifecycleState.Attaching
            or WebViewLifecycleState.Ready;

    private async Task RunQueuedOperationAsync<T>(
        long operationId,
        string operationType,
        DateTimeOffset enqueueTs,
        Func<Task<T>> func,
        TaskCompletionSource<T> tcs)
    {
        var startTs = DateTimeOffset.UtcNow;
        var startThread = Environment.CurrentManagedThreadId;
        try
        {
            var result = await InvokeAsyncOnUiThread(func).ConfigureAwait(false);
            var endTs = DateTimeOffset.UtcNow;
            _logger.LogDebug(
                "Operation success: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}, lifecycleState={LifecycleState}",
                operationId, operationType, enqueueTs, startTs, endTs, startThread, _lifecycleState);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            var classified = ClassifyFailure(ex, operationType, WebViewOperationFailureCategory.AdapterFailed);
            var endTs = DateTimeOffset.UtcNow;
            _logger.LogDebug(
                classified,
                "Operation failed: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}, lifecycleState={LifecycleState}",
                operationId, operationType, enqueueTs, startTs, endTs, startThread, _lifecycleState);
            tcs.TrySetException(classified);
        }
    }

    private Task<T> InvokeAsyncOnUiThread<T>(Func<Task<T>> func)
    {
        if (_disposed)
        {
            return Task.FromException<T>(ClassifyFailure(
                new ObjectDisposedException(nameof(WebViewCore)),
                operationType: "Dispatch",
                defaultCategory: WebViewOperationFailureCategory.Disposed));
        }

        if (_dispatcher.CheckAccess())
        {
            return func();
        }

        return InvokeWithDispatchFailureMappingAsync(func);
    }

    private async Task<T> InvokeWithDispatchFailureMappingAsync<T>(Func<Task<T>> func)
    {
        Task<T> dispatchedTask;
        try
        {
            dispatchedTask = _dispatcher.InvokeAsync(func);
        }
        catch (Exception ex)
        {
            throw ClassifyFailure(ex, operationType: "Dispatch", defaultCategory: WebViewOperationFailureCategory.DispatchFailed);
        }

        return await dispatchedTask.ConfigureAwait(false);
    }

    private static Exception ClassifyFailure(
        Exception exception,
        string operationType,
        WebViewOperationFailureCategory defaultCategory)
    {
        if (WebViewOperationFailure.TryGetCategory(exception, out _))
        {
            return exception;
        }

        var category = exception switch
        {
            ObjectDisposedException => WebViewOperationFailureCategory.Disposed,
            InvalidOperationException invalidOp when
                invalidOp.Message.Contains("not allowed in state", StringComparison.OrdinalIgnoreCase)
                => WebViewOperationFailureCategory.NotReady,
            _ => defaultCategory
        };

        WebViewOperationFailure.SetCategory(exception, category);
        exception.Data["operationType"] = operationType;
        return exception;
    }

    private Task StartNavigationCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke)
        => StartNavigationCoreAsync(requestUri, adapterInvoke, updateSource: true);

    private async Task StartNavigationCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
    {
        var completionTask = await StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource).ConfigureAwait(false);
        await completionTask.ConfigureAwait(false);
    }

    private Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke)
        => StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource: true);

    private async Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
    {
        ThrowIfDisposed();
        ThrowIfNotOnUiThread("async navigation");

        if (updateSource)
        {
            SetSourceInternal(requestUri.AbsoluteUri != AboutBlank.AbsoluteUri ? requestUri : AboutBlank);
        }

        if (_activeNavigation is not null)
        {
            _logger.LogDebug("StartNavigation: superseding active navigation id={NavigationId}", _activeNavigation.NavigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        var operation = new NavigationOperation(navigationId, correlationId: navigationId, requestUri);
        _activeNavigation = operation;

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (API): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        NavigationStarted?.Invoke(this, startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("StartNavigation: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return operation.Task;
        }

        await AwaitNavigationCompletion(navigationId, adapterInvoke).ConfigureAwait(false);

        return operation.Task;
    }

    private async Task AwaitNavigationCompletion(Guid navigationId, Func<Guid, Task> adapterInvoke)
    {
        try
        {
            await adapterInvoke(navigationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "StartNavigation: adapter invocation failed, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Failure, ex);
        }
    }

    private Guid StartCommandNavigation(Uri requestUri)
    {
        if (_activeNavigation is not null)
        {
            _logger.LogDebug("StartCommandNavigation: superseding active navigation id={NavigationId}", _activeNavigation.NavigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        _activeNavigation = new NavigationOperation(navigationId, correlationId: navigationId, requestUri);

        var args = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (command): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        NavigationStarted?.Invoke(this, args);

        if (args.Cancel)
        {
            _logger.LogDebug("StartCommandNavigation: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return Guid.Empty;
        }

        return navigationId;
    }

    private void OnAdapterNavigationCompleted(object? sender, NavigationCompletedEventArgs e)
    {
        _logger.LogDebug("Adapter.NavigationCompleted received: id={NavigationId}, status={Status}, uri={Uri}",
            e.NavigationId, e.Status, e.RequestUri);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => OnAdapterNavigationCompletedOnUiThread(e),
            _logger,
            "Adapter.NavigationCompleted: ignored (disposed or destroyed)");
    }

    private void OnAdapterNavigationCompletedOnUiThread(NavigationCompletedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_activeNavigation is null)
        {
            _logger.LogDebug("Adapter.NavigationCompleted: no active navigation, ignoring id={NavigationId}", e.NavigationId);
            return;
        }

        if (e.NavigationId != _activeNavigation.NavigationId)
        {
            _logger.LogDebug("Adapter.NavigationCompleted: id mismatch (received={Received}, active={Active}), ignoring",
                e.NavigationId, _activeNavigation.NavigationId);
            // Late or unrelated completion; ignore to preserve exactly-once per active NavigationId.
            return;
        }

        var status = e.Status;
        var error = e.Error;

        if (status == NavigationCompletedStatus.Failure && error is null)
        {
            error = new InvalidOperationException("Navigation failed.");
        }

        _activeNavigation.UpdateRequestUri(e.RequestUri);
        CompleteActiveNavigation(status, error);
    }

    private void OnAdapterNewWindowRequested(object? sender, NewWindowRequestedEventArgs e)
    {
        _logger.LogDebug("Event NewWindowRequested: uri={Uri}", e.Uri);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => HandleNewWindowRequestedOnUiThread(e),
            _logger,
            "NewWindowRequested: ignored (disposed or destroyed)");
    }

    private void HandleNewWindowRequestedOnUiThread(NewWindowRequestedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        NewWindowRequested?.Invoke(this, e);

        if (!e.Handled && e.Uri is not null)
        {
            _logger.LogDebug("NewWindowRequested: unhandled, navigating in-view to {Uri}", e.Uri);
            _ = NavigateAsync(e.Uri);
        }
    }

    private void OnAdapterWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        _logger.LogDebug("Event WebMessageReceived: origin={Origin}, channelId={ChannelId}", e.Origin, e.ChannelId);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => OnAdapterWebMessageReceivedOnUiThread(e),
            _logger,
            "WebMessageReceived: ignored (disposed or destroyed)");
    }

    private void OnAdapterWebMessageReceivedOnUiThread(WebMessageReceivedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!_webMessageBridgeEnabled)
        {
            _logger.LogDebug("WebMessageReceived: bridge not enabled, dropping");
            return;
        }

        var policy = _webMessagePolicy;
        if (policy is null)
        {
            _logger.LogDebug("WebMessageReceived: no policy, dropping");
            return;
        }

        var envelope = new WebMessageEnvelope(
            Body: e.Body,
            Origin: e.Origin,
            ChannelId: e.ChannelId,
            ProtocolVersion: e.ProtocolVersion);

        var decision = policy.Evaluate(in envelope);
        if (decision.IsAllowed)
        {
            // Try RPC dispatch first.
            if (_rpcService is not null && _rpcService.TryProcessMessage(e.Body))
            {
                _logger.LogDebug("WebMessageReceived: handled as RPC message");
                return;
            }

            _logger.LogDebug("WebMessageReceived: policy allowed, forwarding");
            WebMessageReceived?.Invoke(this, e);
            return;
        }

        var reason = decision.DropReason ?? WebMessageDropReason.OriginNotAllowed;
        _logger.LogDebug("WebMessageReceived: policy denied, reason={Reason}", reason);
        _webMessageDropDiagnosticsSink?.OnMessageDropped(new WebMessageDropDiagnostic(reason, e.Origin, e.ChannelId));
        _fuloraDiagnosticsSink?.OnEvent(new FuloraDiagnosticsEvent
        {
            EventName = "runtime.webmessage.dropped",
            Layer = "runtime",
            Component = nameof(WebViewCore),
            ChannelId = e.ChannelId.ToString("D"),
            Status = "dropped",
            ErrorType = reason.ToString(),
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = e.Origin,
                ["dropReason"] = reason.ToString()
            }
        });
    }

    private void OnAdapterWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
    {
        _logger.LogDebug("Event WebResourceRequested");

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => WebResourceRequested?.Invoke(this, e));
    }

    private void OnAdapterEnvironmentRequested(object? sender, EnvironmentRequestedEventArgs e)
    {
        _logger.LogDebug("Event EnvironmentRequested");

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => EnvironmentRequested?.Invoke(this, e));
    }

    private void OnAdapterDownloadRequested(object? sender, DownloadRequestedEventArgs e)
    {
        _logger.LogDebug("Event DownloadRequested: uri={Uri}, file={File}", e.DownloadUri, e.SuggestedFileName);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => DownloadRequested?.Invoke(this, e));
    }

    private void OnAdapterPermissionRequested(object? sender, PermissionRequestedEventArgs e)
    {
        _logger.LogDebug("Event PermissionRequested: kind={Kind}, origin={Origin}", e.PermissionKind, e.Origin);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _disposed,
            _adapterDestroyed,
            () => PermissionRequested?.Invoke(this, e));
    }

    private void CompleteActiveNavigation(NavigationCompletedStatus status, Exception? error)
    {
        var operation = _activeNavigation;
        if (operation is null)
        {
            return;
        }

        _activeNavigation = null;

        _logger.LogDebug("Event NavigationCompleted: id={NavigationId}, status={Status}, uri={Uri}, error={Error}",
            operation.NavigationId, status, operation.RequestUri, error?.Message);

        NavigationCompletedEventArgs completedArgs;
        try
        {
            completedArgs = new NavigationCompletedEventArgs(
                operation.NavigationId,
                operation.RequestUri,
                status,
                status == NavigationCompletedStatus.Failure ? error : null);
        }
        catch (Exception ex)
        {
            completedArgs = new NavigationCompletedEventArgs(operation.NavigationId, operation.RequestUri, NavigationCompletedStatus.Failure, ex);
            status = NavigationCompletedStatus.Failure;
            error = ex;
        }

        NavigationCompleted?.Invoke(this, completedArgs);

        if (status == NavigationCompletedStatus.Failure)
        {
            // Preserve categorized exception subclasses from the adapter (Network, SSL, Timeout).
            var faultException = error is WebViewNavigationException navEx
                ? navEx
                : new WebViewNavigationException(
                    message: "Navigation failed.",
                    navigationId: operation.NavigationId,
                    requestUri: operation.RequestUri,
                    innerException: error);
            operation.TrySetFault(faultException);
        }
        else
        {
            if (status == NavigationCompletedStatus.Success)
                ReinjectBridgeStubsIfEnabled();

            operation.TrySetSuccess();
        }
    }

    private void RaiseAdapterDestroyedOnce()
    {
        if (_adapterDestroyed)
        {
            return;
        }

        _adapterDestroyed = true;
        _logger.LogDebug("AdapterDestroyed: raising");
        AdapterDestroyed?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfNotOnUiThread(string apiName)
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException($"'{apiName}' must be called on the UI thread.");
        }
    }

    private void ObserveBackgroundTask(Task task, string operationType)
    {
        task.ContinueWith(
            t =>
            {
                if (t.Exception is null)
                {
                    return;
                }

                var error = t.Exception.InnerException ?? t.Exception;
                _logger.LogDebug(error, "Background operation faulted: {OperationType}", operationType);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(WebViewCore));
    }

    private void SetSourceInternal(Uri uri)
    {
        _source = uri;
    }

    /// <summary>
    /// Runtime wrapper around <see cref="ICookieAdapter"/> that adds lifecycle guards and dispatcher marshaling.
    /// </summary>
    private sealed class RuntimeCookieManager : ICookieManager
    {
        private readonly ICookieAdapter _cookieAdapter;
        private readonly WebViewCore _owner;
        private readonly ILogger _logger;

        public RuntimeCookieManager(ICookieAdapter cookieAdapter, WebViewCore owner, ILogger logger)
        {
            _cookieAdapter = cookieAdapter;
            _owner = owner;
            _logger = logger;
        }

        public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ThrowIfOwnerDisposed();
            _logger.LogDebug("CookieManager.GetCookiesAsync: {Uri}", uri);
            return _owner.EnqueueOperationAsync("Cookie.GetCookiesAsync", () => _cookieAdapter.GetCookiesAsync(uri));
        }

        public Task SetCookieAsync(WebViewCookie cookie)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            ThrowIfOwnerDisposed();
            _logger.LogDebug("CookieManager.SetCookieAsync: {Name}@{Domain}", cookie.Name, cookie.Domain);
            return _owner.EnqueueOperationAsync("Cookie.SetCookieAsync", () => _cookieAdapter.SetCookieAsync(cookie));
        }

        public Task DeleteCookieAsync(WebViewCookie cookie)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            ThrowIfOwnerDisposed();
            _logger.LogDebug("CookieManager.DeleteCookieAsync: {Name}@{Domain}", cookie.Name, cookie.Domain);
            return _owner.EnqueueOperationAsync("Cookie.DeleteCookieAsync", () => _cookieAdapter.DeleteCookieAsync(cookie));
        }

        public Task ClearAllCookiesAsync()
        {
            ThrowIfOwnerDisposed();
            _logger.LogDebug("CookieManager.ClearAllCookiesAsync");
            return _owner.EnqueueOperationAsync("Cookie.ClearAllCookiesAsync", () => _cookieAdapter.ClearAllCookiesAsync());
        }

        private void ThrowIfOwnerDisposed()
        {
            ObjectDisposedException.ThrowIf(_owner._disposed, nameof(WebViewCore));
        }
    }

    /// <summary>
    /// Runtime wrapper around <see cref="ICommandAdapter"/> that delegates editing commands.
    /// </summary>
    private sealed class RuntimeCommandManager : ICommandManager
    {
        private readonly ICommandAdapter _commandAdapter;
        private readonly WebViewCore _owner;

        public RuntimeCommandManager(ICommandAdapter commandAdapter, WebViewCore owner)
        {
            _commandAdapter = commandAdapter;
            _owner = owner;
        }

        public Task CopyAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Copy));

        public Task CutAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Cut));

        public Task PasteAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Paste));

        public Task SelectAllAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.SelectAll));

        public Task UndoAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Undo));

        public Task RedoAsync() => ToVoidTask(ExecuteAsync(WebViewCommand.Redo));

        private static Task ToVoidTask(Task<object?> task) => task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                throw t.Exception!.GetBaseException();
        }, TaskContinuationOptions.ExecuteSynchronously);

        private Task<object?> ExecuteAsync(WebViewCommand command)
        {
            return _owner.EnqueueOperationAsync($"Command.{command}", () =>
            {
                _commandAdapter.ExecuteCommand(command);
                return Task.CompletedTask;
            });
        }
    }

    private enum WebViewLifecycleState
    {
        Created,
        Attaching,
        Ready,
        Detaching,
        Disposed
    }

    private sealed class NavigationOperation
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NavigationOperation(Guid navigationId, Guid correlationId, Uri requestUri)
        {
            NavigationId = navigationId;
            CorrelationId = correlationId;
            RequestUri = requestUri;
        }

        public Guid NavigationId { get; }
        public Guid CorrelationId { get; }
        public Uri RequestUri { get; private set; }

        public Task Task => _tcs.Task;

        public void UpdateRequestUri(Uri requestUri) => RequestUri = requestUri;

        public void TrySetSuccess() => _tcs.TrySetResult();

        public void TrySetFault(Exception ex) => _tcs.TrySetException(ex);
    }
}
