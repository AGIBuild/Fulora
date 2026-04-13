using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Core runtime implementation of <see cref="IWebView"/> over a platform adapter.
/// </summary>
public sealed class WebViewCore : ISpaHostingWebView, IWebViewAdapterHost, IWebViewCoreFeatureHost, IWebViewCoreBridgeHost, IWebViewCoreAdapterEventHost, IWebViewCoreNavigationHost, IDisposable
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
    private readonly WebViewCoreFeatureRuntime _featureRuntime;
    private readonly WebViewCoreCapabilityRuntime _capabilityRuntime;

    /// <summary>Whether the current adapter supports drag-and-drop.</summary>
    internal bool HasDragDropSupport => _featureRuntime.HasDragDropSupport;

    private readonly WebViewCoreBridgeRuntime _bridgeRuntime;
    private readonly WebViewCoreAdapterEventRuntime _adapterEventRuntime;
    private readonly WebViewCoreNavigationRuntime _navigationRuntime;
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

        _featureRuntime = new WebViewCoreFeatureRuntime(this, _adapter, _dispatcher, _logger, _environmentOptions);
        _bridgeRuntime = new WebViewCoreBridgeRuntime(this, _logger, _environmentOptions.EnableDevTools);
        _capabilityRuntime = new WebViewCoreCapabilityRuntime(_featureRuntime, _bridgeRuntime, _cookieManager, _commandManager);
        _adapterEventRuntime = new WebViewCoreAdapterEventRuntime(this, _dispatcher, _logger);
        _navigationRuntime = new WebViewCoreNavigationRuntime(this, _dispatcher, _logger);

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
        if (_activeNavigation is not null)
        {
            _logger.LogDebug("Dispose: faulting active navigation id={NavigationId}", _activeNavigation.NavigationId);
            // After disposal, async APIs must not hang. No events must be raised.
            _activeNavigation.TrySetFault(new ObjectDisposedException(nameof(WebViewCore)));
            _activeNavigation = null;
        }

        _featureRuntime.Dispose();

        _bridgeRuntime.Dispose();

        _spaHostingService?.Dispose();
        _spaHostingService = null;

        _logger.LogDebug("Dispose: completed");
    }

    /// <inheritdoc />
    public Task NavigateAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _logger.LogDebug("NavigateAsync: {Uri}", uri);

        return EnqueueOperationAsync(nameof(NavigateAsync), () => _navigationRuntime.StartNavigationRequestCoreAsync(
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

        return EnqueueOperationAsync(nameof(NavigateToStringAsync), () => _navigationRuntime.StartNavigationRequestCoreAsync(
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

        var navigationId = _navigationRuntime.StartCommandNavigation(requestUri: Source);
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

        var navigationId = _navigationRuntime.StartCommandNavigation(requestUri: Source);
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

        var navigationId = _navigationRuntime.StartCommandNavigation(requestUri: Source);
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
        => _navigationRuntime.OnNativeNavigationStartingAsync(info);

    private NativeNavigationStartingDecision OnNativeNavigationStartingOnUiThread(NativeNavigationStartingInfo info)
        => _navigationRuntime.OnNativeNavigationStartingOnUiThread(info);

    /// <inheritdoc />
    public ICookieManager? TryGetCookieManager() => _capabilityRuntime.TryGetCookieManager();

    /// <inheritdoc />
    public ICommandManager? TryGetCommandManager() => _capabilityRuntime.TryGetCommandManager();

    /// <inheritdoc />
    public IWebViewRpcService? Rpc => _capabilityRuntime.Rpc;

    // ==================== DevTools ====================

    /// <inheritdoc />
    public Task OpenDevToolsAsync()
        => _capabilityRuntime.OpenDevToolsAsync();

    /// <inheritdoc />
    public Task CloseDevToolsAsync()
        => _capabilityRuntime.CloseDevToolsAsync();

    /// <inheritdoc />
    public Task<bool> IsDevToolsOpenAsync()
        => _capabilityRuntime.IsDevToolsOpenAsync();

    // ==================== Bridge ====================

    /// <inheritdoc />
    public IBridgeTracer? BridgeTracer
    {
        get => _capabilityRuntime.BridgeTracer;
        set => _capabilityRuntime.BridgeTracer = value;
    }

    /// <inheritdoc />
    public IBridgeService Bridge
        => _capabilityRuntime.Bridge;

    /// <inheritdoc />
    public Task<byte[]> CaptureScreenshotAsync()
        => _capabilityRuntime.CaptureScreenshotAsync();

    /// <inheritdoc />
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
        => _capabilityRuntime.PrintToPdfAsync(options);

    // ==================== Zoom ====================

    /// <summary>Raised when the zoom factor changes.</summary>
    public event EventHandler<double>? ZoomFactorChanged;

    /// <summary>
    /// Gets or sets the zoom factor (1.0 = 100%). Clamped to [0.25, 5.0].
    /// Returns 1.0 if the adapter does not support zoom.
    /// </summary>
    public Task<double> GetZoomFactorAsync()
        => _capabilityRuntime.GetZoomFactorAsync();

    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor)
        => _capabilityRuntime.SetZoomFactorAsync(zoomFactor);

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

    /// <summary>
    /// Searches the current page for the given text.
    /// </summary>
    /// <param name="text">The search text. Must not be null or empty.</param>
    /// <param name="options">Optional search options (case sensitivity, direction).</param>
    /// <returns>A <see cref="FindInPageEventArgs"/> with match count and active index.</returns>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IFindInPageAdapter"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
        => _capabilityRuntime.FindInPageAsync(text, options);

    /// <summary>
    /// Clears find-in-page highlights and resets search state.
    /// </summary>
    /// <param name="clearHighlights">Whether to remove visual highlights. Default: true.</param>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IFindInPageAdapter"/>.</exception>
    public Task StopFindInPageAsync(bool clearHighlights = true)
        => _capabilityRuntime.StopFindInPageAsync(clearHighlights);

    /// <summary>
    /// Registers a JavaScript snippet to run at document start on every page load.
    /// </summary>
    /// <param name="javaScript">The script to inject.</param>
    /// <returns>An opaque script ID that can be passed to <see cref="RemovePreloadScriptAsync"/>.</returns>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IPreloadScriptAdapter"/>.</exception>
    public Task<string> AddPreloadScriptAsync(string javaScript)
        => _capabilityRuntime.AddPreloadScriptAsync(javaScript);

    /// <summary>
    /// Removes a previously registered preload script by its ID.
    /// </summary>
    /// <param name="scriptId">The ID returned by <see cref="AddPreloadScriptAsync"/>.</param>
    /// <exception cref="NotSupportedException">The adapter does not implement <see cref="IPreloadScriptAdapter"/>.</exception>
    public Task RemovePreloadScriptAsync(string scriptId)
        => _capabilityRuntime.RemovePreloadScriptAsync(scriptId);

    /// <summary>
    /// Asynchronously retrieves the native platform WebView handle.
    /// This is the primary any-thread API surface and always marshals adapter access to the UI thread.
    /// </summary>
    public Task<INativeHandle?> TryGetWebViewHandleAsync()
        => _capabilityRuntime.TryGetWebViewHandleAsync();

    /// <summary>
    /// Compatibility wrapper around <see cref="TryGetWebViewHandleAsync"/> for synchronous call sites.
    /// Prefer using <see cref="TryGetWebViewHandleAsync"/> to avoid blocking.
    /// </summary>
    public INativeHandle? TryGetWebViewHandle()
        => TryGetWebViewHandleAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Sets the custom User-Agent string at runtime.
    /// Pass <c>null</c> to revert to the platform default.
    /// </summary>
    public void SetCustomUserAgent(string? userAgent)
        => _capabilityRuntime.SetCustomUserAgent(userAgent);

    /// <inheritdoc />
    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
        => _capabilityRuntime.EnableWebMessageBridge(options);

    /// <inheritdoc />
    public void DisableWebMessageBridge()
        => _capabilityRuntime.DisableWebMessageBridge();

    /// <summary>
    /// Re-injects the base RPC JS stub and all exposed service stubs when the bridge is enabled.
    /// Called after successful navigation to restore <c>window.agWebView</c> in the new JS context.
    /// </summary>
    internal void ReinjectBridgeStubsIfEnabled()
        => _capabilityRuntime.ReinjectBridgeStubsIfEnabled();

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
        if (options.AutoInjectBridgeScript && !_bridgeRuntime.IsBridgeEnabled)
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
        => _navigationRuntime.StartNavigationCoreAsync(requestUri, adapterInvoke, updateSource: true);

    private async Task StartNavigationCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
        => await _navigationRuntime.StartNavigationCoreAsync(requestUri, adapterInvoke, updateSource).ConfigureAwait(false);

    private Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke)
        => _navigationRuntime.StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource: true);

    private async Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
        => await _navigationRuntime.StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource).ConfigureAwait(false);

    private void OnAdapterNavigationCompleted(object? sender, NavigationCompletedEventArgs e)
        => _navigationRuntime.HandleAdapterNavigationCompleted(e);

    private void OnAdapterNavigationCompletedOnUiThread(NavigationCompletedEventArgs e)
        => _navigationRuntime.HandleAdapterNavigationCompletedOnUiThread(e);

    private void OnAdapterNewWindowRequested(object? sender, NewWindowRequestedEventArgs e)
        => _adapterEventRuntime.HandleAdapterNewWindowRequested(e);

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
        => _bridgeRuntime.HandleAdapterWebMessageReceivedOnUiThread(e);

    private void OnAdapterWebResourceRequested(object? sender, WebResourceRequestedEventArgs e)
        => _adapterEventRuntime.HandleAdapterWebResourceRequested(e);

    private void OnAdapterEnvironmentRequested(object? sender, EnvironmentRequestedEventArgs e)
        => _adapterEventRuntime.HandleAdapterEnvironmentRequested(e);

    private void OnAdapterDownloadRequested(object? sender, DownloadRequestedEventArgs e)
        => _adapterEventRuntime.HandleAdapterDownloadRequested(e);

    private void OnAdapterPermissionRequested(object? sender, PermissionRequestedEventArgs e)
        => _adapterEventRuntime.HandleAdapterPermissionRequested(e);

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

    bool IWebViewCoreFeatureHost.IsDisposed => _disposed;

    bool IWebViewCoreFeatureHost.IsAdapterDestroyed => _adapterDestroyed;

    bool IWebViewCoreBridgeHost.IsDisposed => _disposed;

    Guid IWebViewCoreBridgeHost.ChannelId => ChannelId;

    Task IWebViewCoreFeatureHost.EnqueueOperationAsync(string operationType, Func<Task> func)
        => EnqueueOperationAsync(operationType, func);

    Task<T> IWebViewCoreFeatureHost.EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func)
        => EnqueueOperationAsync(operationType, func);

    void IWebViewCoreFeatureHost.ObserveBackgroundTask(Task task, string operationType)
        => ObserveBackgroundTask(task, operationType);

    void IWebViewCoreFeatureHost.ThrowIfDisposed()
        => ThrowIfDisposed();

    void IWebViewCoreFeatureHost.RaiseZoomFactorChanged(double zoomFactor)
        => ZoomFactorChanged?.Invoke(this, zoomFactor);

    void IWebViewCoreFeatureHost.RaiseContextMenuRequested(ContextMenuRequestedEventArgs args)
        => ContextMenuRequested?.Invoke(this, args);

    void IWebViewCoreFeatureHost.RaiseDragEntered(DragEventArgs args)
        => DragEntered?.Invoke(this, args);

    void IWebViewCoreFeatureHost.RaiseDragOver(DragEventArgs args)
        => DragOver?.Invoke(this, args);

    void IWebViewCoreFeatureHost.RaiseDragLeft()
        => DragLeft?.Invoke(this, EventArgs.Empty);

    void IWebViewCoreFeatureHost.RaiseDropCompleted(DropEventArgs args)
        => DropCompleted?.Invoke(this, args);

    Task<string?> IWebViewCoreBridgeHost.InvokeScriptAsync(string script)
        => InvokeScriptAsync(script);

    void IWebViewCoreBridgeHost.ObserveBackgroundTask(Task task, string operationType)
        => ObserveBackgroundTask(task, operationType);

    void IWebViewCoreBridgeHost.RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
        => WebMessageReceived?.Invoke(this, args);

    void IWebViewCoreBridgeHost.ThrowIfDisposed()
        => ThrowIfDisposed();

    void IWebViewCoreBridgeHost.ThrowIfNotOnUiThread(string apiName)
        => ThrowIfNotOnUiThread(apiName);

    bool IWebViewCoreAdapterEventHost.IsDisposed => _disposed;

    bool IWebViewCoreAdapterEventHost.IsAdapterDestroyed => _adapterDestroyed;

    Task IWebViewCoreAdapterEventHost.NavigateAsync(Uri uri)
        => NavigateAsync(uri);

    void IWebViewCoreAdapterEventHost.RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
        => NewWindowRequested?.Invoke(this, args);

    void IWebViewCoreAdapterEventHost.RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
        => WebResourceRequested?.Invoke(this, args);

    void IWebViewCoreAdapterEventHost.RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args)
        => EnvironmentRequested?.Invoke(this, args);

    void IWebViewCoreAdapterEventHost.RaiseDownloadRequested(DownloadRequestedEventArgs args)
        => DownloadRequested?.Invoke(this, args);

    void IWebViewCoreAdapterEventHost.RaisePermissionRequested(PermissionRequestedEventArgs args)
        => PermissionRequested?.Invoke(this, args);

    bool IWebViewCoreNavigationHost.IsDisposed => _disposed;

    bool IWebViewCoreNavigationHost.IsAdapterDestroyed => _adapterDestroyed;

    void IWebViewCoreNavigationHost.CompleteActiveNavigation(NavigationCompletedStatus status, Exception? error)
        => CompleteActiveNavigation(status, error);

    void IWebViewCoreNavigationHost.RaiseNavigationStarting(NavigationStartingEventArgs args)
        => NavigationStarted?.Invoke(this, args);

    Task IWebViewCoreNavigationHost.SetActiveNavigation(Guid navigationId, Guid correlationId, Uri requestUri)
    {
        var operation = new NavigationOperation(navigationId, correlationId, requestUri);
        _activeNavigation = operation;
        return operation.Task;
    }

    void IWebViewCoreNavigationHost.SetSource(Uri uri)
        => SetSourceInternal(uri);

    void IWebViewCoreNavigationHost.ThrowIfNotOnUiThread(string apiName)
        => ThrowIfNotOnUiThread(apiName);

    bool IWebViewCoreNavigationHost.TryGetActiveNavigation(out WebViewCoreNavigationState state)
    {
        if (_activeNavigation is { } operation)
        {
            state = new WebViewCoreNavigationState(
                operation.NavigationId,
                operation.CorrelationId,
                operation.RequestUri);
            return true;
        }

        state = default;
        return false;
    }

    void IWebViewCoreNavigationHost.UpdateActiveNavigationRequestUri(Uri requestUri)
        => _activeNavigation?.UpdateRequestUri(requestUri);

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
