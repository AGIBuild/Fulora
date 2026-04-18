using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// Internal runtime implementation of <see cref="IWebView"/> over a platform adapter.
/// <para>
/// Consumers must construct instances via <see cref="WebViewFactory"/> (non-DI scenarios) or the
/// DI-registered <c>Func&lt;IWebViewDispatcher, IWebView&gt;</c> delegate and interact only with
/// <see cref="IWebView"/> and its capability interfaces. This class is intentionally
/// <see langword="internal"/> to keep the public surface minimal and let the coordinator evolve
/// without breaking downstream callers.
/// </para>
/// </summary>
internal sealed class WebViewCore : ISpaHostingWebView, IWebViewCoreControlEvents, IWebViewAdapterHost, IWebViewCoreFeatureHost, IWebViewCoreBridgeHost, IWebViewCoreAdapterEventHost, IWebViewCoreNavigationHost, IWebViewCoreSpaHostingHost, IWebViewCoreOperationHost, IDisposable
{
    private static readonly Uri AboutBlank = new("about:blank");

    private readonly IWebViewAdapter _adapter;
    private readonly AdapterCapabilities _capabilities;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger<WebViewCore> _logger;
    private readonly IWebViewEnvironmentOptions _environmentOptions;
    private readonly WebViewCoreCapabilityDetectionRuntime _capabilityDetectionRuntime;
    private readonly object _operationQueueLock = new();
    private Task _operationQueueTail = Task.CompletedTask;
    private long _operationSequence;

    /// <summary>
    /// Internal default-adapter factory entry used by <see cref="WebViewFactory.CreateDefault(IWebViewDispatcher, ILoggerFactory?)"/>
    /// and the DI registrations in <c>AddFulora</c>. Consumers should go through those
    /// entry points rather than referencing this type directly.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal static IWebView CreateDefault(IWebViewDispatcher dispatcher, ILogger<WebViewCore> logger)
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
    private Uri _source;
    private volatile WebViewLifecycleState _lifecycleState = WebViewLifecycleState.Created;

    private readonly ICookieManager? _cookieManager;
    private readonly ICommandManager? _commandManager;
    private readonly WebViewCoreFeatureRuntime _featureRuntime;
    private readonly WebViewCoreCapabilityRuntime _capabilityRuntime;
    private readonly WebViewCoreEventWiringRuntime _eventWiringRuntime;

    /// <summary>Whether the current adapter supports drag-and-drop.</summary>
    internal bool HasDragDropSupport => _featureRuntime.HasDragDropSupport;

    private readonly WebViewCoreBridgeRuntime _bridgeRuntime;
    private readonly WebViewCoreAdapterEventRuntime _adapterEventRuntime;
    private readonly WebViewCoreNavigationRuntime _navigationRuntime;
    private readonly WebViewCoreSpaHostingRuntime _spaHostingRuntime;

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

        // One-shot capability negotiation: probe every optional adapter interface exactly once
        // here, then pass the resulting value object to every runtime that needs feature gating.
        // No other site in the codebase should perform `adapter as IXxxAdapter` tests.
        _capabilities = AdapterCapabilities.From(_adapter);

        _capabilityDetectionRuntime = new WebViewCoreCapabilityDetectionRuntime(_capabilities, _environmentOptions, _logger);

        _capabilityDetectionRuntime.ApplyEnvironmentOptions();
        _cookieManager = _capabilityDetectionRuntime.CreateCookieManager(this);
        _capabilityDetectionRuntime.RegisterConfiguredCustomSchemes();
        _commandManager = _capabilityDetectionRuntime.CreateCommandManager(this);

        _featureRuntime = new WebViewCoreFeatureRuntime(this, _adapter, _capabilities, _dispatcher, _logger, _environmentOptions);
        _bridgeRuntime = new WebViewCoreBridgeRuntime(this, _dispatcher, _logger, _environmentOptions.EnableDevTools);
        _capabilityRuntime = new WebViewCoreCapabilityRuntime(_featureRuntime, _bridgeRuntime, _cookieManager, _commandManager);
        _adapterEventRuntime = new WebViewCoreAdapterEventRuntime(this, _dispatcher, _logger);
        _navigationRuntime = new WebViewCoreNavigationRuntime(this, _dispatcher, _logger);
        _spaHostingRuntime = new WebViewCoreSpaHostingRuntime(this, _logger);
        _eventWiringRuntime = new WebViewCoreEventWiringRuntime(
            _adapter,
            _capabilities,
            _logger,
            new WebViewAdapterEventRouter(
                OnNavigationCompleted: _navigationRuntime.HandleAdapterNavigationCompleted,
                OnNewWindowRequested: _adapterEventRuntime.HandleAdapterNewWindowRequested,
                OnWebMessageReceived: _bridgeRuntime.HandleAdapterWebMessageReceived,
                OnWebResourceRequested: _adapterEventRuntime.HandleAdapterWebResourceRequested,
                OnEnvironmentRequested: _adapterEventRuntime.HandleAdapterEnvironmentRequested,
                OnDownloadRequested: _adapterEventRuntime.HandleAdapterDownloadRequested,
                OnPermissionRequested: _adapterEventRuntime.HandleAdapterPermissionRequested));
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
            var navigationTask = _navigationRuntime.StartNavigationCoreAsync(
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
    public bool IsLoading => _navigationRuntime.IsLoading;

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

        // --- Owned IDisposable runtimes (disposed in reverse dependency order) ---
        // 1) EventWiring first: detaches adapter event handlers so no late callbacks can observe
        //    partially-torn-down runtimes.
        _eventWiringRuntime.Dispose();

        // After adapter events are unhooked, the NavigationRuntime owns any active navigation — fault
        // it silently so async callers do not hang. No events are raised (FaultActiveForDispose is
        // explicitly event-free, since observers are no longer welcome post-dispose).
        _navigationRuntime.FaultActiveForDispose(new ObjectDisposedException(nameof(WebViewCore)));

        // 2) Feature / Bridge / SpaHosting each own native handles, bridge subscriptions, or
        //    service-worker state that must be torn down explicitly.
        _featureRuntime.Dispose();
        _bridgeRuntime.Dispose();
        _spaHostingRuntime.Dispose();

        // --- Non-IDisposable runtimes (intentionally NOT disposed) ---
        // _capabilityDetectionRuntime / _capabilityRuntime / _adapterEventRuntime / _navigationRuntime
        // hold only injected references (and NavigationRuntime's active-op is already faulted above);
        // their own types document why Dispose() is absent. Do not add "defensive" disposal here —
        // it would invert ownership.

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
            _navigationRuntime.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
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
            _navigationRuntime.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
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
            _navigationRuntime.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
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

        if (!_navigationRuntime.IsLoading)
        {
            _logger.LogDebug("Stop: no active navigation");
            return false;
        }

        // The return value reflects the state on entry — "Stop accepted because there was an
        // active navigation". _adapter.Stop() may itself synchronously raise NavigationCompleted
        // (which drains the runtime's active-op via HandleAdapterNavigationCompleted); in that
        // case TryStopActiveNavigation returns false because there is nothing left to cancel.
        // Either way, the caller learns "yes, a stop was honored for an in-flight navigation".
        _adapter.Stop();
        _navigationRuntime.TryStopActiveNavigation();
        return true;
    }

    ValueTask<NativeNavigationStartingDecision> IWebViewAdapterHost.OnNativeNavigationStartingAsync(NativeNavigationStartingInfo info)
        => _navigationRuntime.OnNativeNavigationStartingAsync(info);

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

    bool IWebViewCoreSpaHostingHost.IsBridgeEnabled => _bridgeRuntime.IsBridgeEnabled;

    void IWebViewCoreSpaHostingHost.RegisterCustomScheme(CustomSchemeRegistration registration)
    {
        _capabilities.CustomScheme?.RegisterCustomSchemes([registration]);
    }

    void IWebViewCoreSpaHostingHost.AddWebResourceRequestedHandler(EventHandler<WebResourceRequestedEventArgs> handler)
        => WebResourceRequested += handler;

    void IWebViewCoreSpaHostingHost.RemoveWebResourceRequestedHandler(EventHandler<WebResourceRequestedEventArgs> handler)
        => WebResourceRequested -= handler;

    // ==================== SPA Hosting ====================

    /// <summary>
    /// Enables SPA hosting. Registers the custom scheme, subscribes to WebResourceRequested,
    /// and optionally auto-enables the bridge.
    /// </summary>
    public void EnableSpaHosting(SpaHostingOptions options)
        => _spaHostingRuntime.EnableSpaHosting(options);

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

    // Non-async by design: try/return + catch/return makes both code paths return a Task<T>,
    // which eliminates the CS0165 fragility of the previous `async` version (where the compiler
    // could not prove that `dispatchedTask` was assigned before `await` unless the catch block
    // always threw). A future edit that accidentally removes the throw (e.g. replaces with log +
    // return) now fails to compile instead of leaking an unassigned local.
    private Task<T> InvokeWithDispatchFailureMappingAsync<T>(Func<Task<T>> func)
    {
        try
        {
            return _dispatcher.InvokeAsync(func);
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ClassifyFailure(
                ex,
                operationType: "Dispatch",
                defaultCategory: WebViewOperationFailureCategory.DispatchFailed));
        }
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

    bool IWebViewCoreLifecycleHost.IsDisposed => _disposed;

    bool IWebViewCoreLifecycleHost.IsAdapterDestroyed => _adapterDestroyed;

    void IWebViewCoreDisposalHost.ThrowIfDisposed() => ThrowIfDisposed();

    void IWebViewCoreBackgroundTaskObserver.ObserveBackgroundTask(Task task, string operationType)
        => ObserveBackgroundTask(task, operationType);

    Guid IWebViewCoreBridgeHost.ChannelId => ChannelId;

    Task IWebViewCoreFeatureHost.EnqueueOperationAsync(string operationType, Func<Task> func)
        => EnqueueOperationAsync(operationType, func);

    Task<T> IWebViewCoreFeatureHost.EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func)
        => EnqueueOperationAsync(operationType, func);

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

    void IWebViewCoreBridgeHost.RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
        => WebMessageReceived?.Invoke(this, args);

    void IWebViewCoreBridgeHost.ThrowIfNotOnUiThread(string apiName)
        => ThrowIfNotOnUiThread(apiName);

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

    void IWebViewCoreNavigationHost.RaiseNavigationStarting(NavigationStartingEventArgs args)
        => NavigationStarted?.Invoke(this, args);

    void IWebViewCoreNavigationHost.RaiseNavigationCompleted(NavigationCompletedEventArgs args)
        => NavigationCompleted?.Invoke(this, args);

    void IWebViewCoreNavigationHost.ReinjectBridgeStubsIfEnabled()
        => ReinjectBridgeStubsIfEnabled();

    void IWebViewCoreNavigationHost.SetSource(Uri uri)
        => SetSourceInternal(uri);

    void IWebViewCoreNavigationHost.ThrowIfNotOnUiThread(string apiName)
        => ThrowIfNotOnUiThread(apiName);

    private void SetSourceInternal(Uri uri)
    {
        _source = uri;
    }

    // ==================== IWebViewCoreOperationHost (for RuntimeCookieManager / RuntimeCommandManager) ====================

    Task<T> IWebViewCoreOperationHost.EnqueueOperationAsync<T>(string operationType, Func<Task<T>> func)
        => EnqueueOperationAsync(operationType, func);

    Task<object?> IWebViewCoreOperationHost.EnqueueOperationAsync(string operationType, Func<Task> func)
        => EnqueueOperationAsync(operationType, func);

    private enum WebViewLifecycleState
    {
        Created,
        Attaching,
        Ready,
        Detaching,
        Disposed
    }
}
