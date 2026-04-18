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
/// <para>
/// Composition: <see cref="WebViewCore"/> owns a single <see cref="WebViewCoreContext"/> (shared
/// adapter / dispatcher / logger / lifecycle / events / operation queue) and a set of focused
/// runtime objects (feature, bridge, navigation, SPA hosting, adapter-event translation). Public
/// events delegate add/remove to <see cref="WebViewCoreContext.Events"/> so the core itself holds
/// no <c>EventHandler&lt;T&gt;</c> fields and no per-runtime host-interface forwarders.
/// </para>
/// </summary>
internal sealed class WebViewCore : ISpaHostingWebView, IWebViewCoreControlEvents, IWebViewAdapterHost, IDisposable
{
    private static readonly Uri AboutBlank = new("about:blank");

    // Centralised lifecycle state (disposal latch, adapter-destroyed at-most-once flag, phase marker).
    // All three flags used to live on WebViewCore directly; the machine owns them so the admission
    // rule and at-most-once semantics exist in exactly one place.
    private readonly WebViewLifecycleStateMachine _lifecycle = new();

    private readonly IWebViewAdapter _adapter;
    private readonly ILogger<WebViewCore> _logger;
    private readonly WebViewCoreContext _context;
    private readonly WebViewCoreEventHub _events;
    private readonly WebViewCoreOperationQueue _operationQueue;

    private readonly ICookieManager _cookieManager;
    private readonly ICommandManager _commandManager;
    private readonly WebViewCoreFeatureRuntime _featureRuntime;
    private readonly WebViewCoreBridgeRuntime _bridgeRuntime;
    private readonly WebViewCoreAdapterEventRuntime _adapterEventRuntime;
    private readonly WebViewCoreNavigationRuntime _navigationRuntime;
    private readonly WebViewCoreSpaHostingRuntime _spaHostingRuntime;
    private readonly WebViewCoreEventWiringRuntime _eventWiringRuntime;

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
        ArgumentNullException.ThrowIfNull(dispatcher);
        _logger = logger ?? NullLogger<WebViewCore>.Instance;
        var envOptions = environmentOptions ?? WebViewEnvironment.Options;

        ChannelId = Guid.NewGuid();

        _logger.LogDebug("WebViewCore created: channelId={ChannelId}, adapter={AdapterType}",
            ChannelId, adapter.GetType().FullName);

        _adapter.Initialize(this);
        _logger.LogDebug("Adapter initialized");

        // Mandatory capabilities (cookies, commands, zoom, preload, etc.) are inherited by
        // IWebViewAdapter itself — no negotiation needed. Only the two truly-optional facets
        // (drag-drop, async-preload) are probed into AdapterCapabilities, exactly once.
        // No other site in the codebase should perform `adapter as IXxxAdapter` tests.
        var capabilities = AdapterCapabilities.From(_adapter);

        _events = new WebViewCoreEventHub(this);
        _operationQueue = new WebViewCoreOperationQueue(_lifecycle, dispatcher, _logger);
        _context = new WebViewCoreContext(
            adapter: _adapter,
            capabilities: capabilities,
            dispatcher: dispatcher,
            logger: _logger,
            environmentOptions: envOptions,
            lifecycle: _lifecycle,
            events: _events,
            operations: _operationQueue,
            channelId: ChannelId);

        var capabilityDetection = new WebViewCoreCapabilityDetectionRuntime(_context);
        capabilityDetection.ApplyEnvironmentOptions();
        _cookieManager = capabilityDetection.CreateCookieManager();
        capabilityDetection.RegisterConfiguredCustomSchemes();
        _commandManager = capabilityDetection.CreateCommandManager();

        _featureRuntime = new WebViewCoreFeatureRuntime(_context);
        _bridgeRuntime = new WebViewCoreBridgeRuntime(_context, envOptions.EnableDevTools);
        _adapterEventRuntime = new WebViewCoreAdapterEventRuntime(_context, NavigateAsync);
        _navigationRuntime = new WebViewCoreNavigationRuntime(_context, _bridgeRuntime.ReinjectBridgeStubsIfEnabled);
        _spaHostingRuntime = new WebViewCoreSpaHostingRuntime(_context, _bridgeRuntime);
        _eventWiringRuntime = new WebViewCoreEventWiringRuntime(
            _adapter,
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

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal void Attach(INativeHandle parentHandle)
    {
        _lifecycle.TransitionToAttaching();
        _logger.LogDebug("Attach: parentHandle.HandleDescriptor={Descriptor}", parentHandle.HandleDescriptor);
        _adapter.Attach(parentHandle);
        _lifecycle.TransitionToReady();
        _logger.LogDebug("Attach: completed");

        // Raise AdapterCreated after successful attach, before any pending navigation.
        var handle = TryGetWebViewHandle();
        _logger.LogDebug("AdapterCreated: raising with handle={HasHandle}", handle is not null);
        _events.RaiseAdapterCreated(new AdapterCreatedEventArgs(handle));
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    internal void Detach()
    {
        _lifecycle.TransitionToDetaching();
        _logger.LogDebug("Detach: begin");
        RaiseAdapterDestroyedOnce();
        _adapter.Detach();
        _logger.LogDebug("Detach: completed");
    }

    /// <inheritdoc />
    public Uri Source
    {
        get => _navigationRuntime.CurrentSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _context.ThrowIfDisposed();
            _context.ThrowIfNotOnUiThread(nameof(Source));

            _logger.LogDebug("Source set: {Uri}", value);

            // The navigation runtime owns the observable source state. Source-set semantics keep
            // the sync public surface while starting a background navigation; the navigation task
            // is observed for faults but not awaited.
            var navigationTask = _navigationRuntime.StartNavigationCoreAsync(
                requestUri: value,
                adapterInvoke: navigationId => _adapter.NavigateAsync(navigationId, value),
                updateSource: true);
            _context.ObserveBackgroundTask(navigationTask, nameof(Source));
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

    /// <summary>Whether the current adapter supports drag-and-drop.</summary>
    internal bool HasDragDropSupport => _featureRuntime.HasDragDropSupport;

    /// <summary>
    /// Internal accessor exposing the event hub for integration tests that simulate adapter-raised
    /// events post-dispose to verify downstream subscribers have been detached. Production code
    /// must never use this property — subscribe through the public event surface instead.
    /// </summary>
    internal WebViewCoreEventHub Events => _events;

    // ==================== Observable events ====================
    // All observable events are owned by the hub; add/remove is delegated so WebViewCore itself
    // holds zero EventHandler<T> fields and every raise site goes through a single Raise method.

    /// <inheritdoc />
    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted
    {
        add => _events.NavigationStarted += value;
        remove => _events.NavigationStarted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted
    {
        add => _events.NavigationCompleted += value;
        remove => _events.NavigationCompleted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested
    {
        add => _events.NewWindowRequested += value;
        remove => _events.NewWindowRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived
    {
        add => _events.WebMessageReceived += value;
        remove => _events.WebMessageReceived -= value;
    }

    /// <inheritdoc />
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested
    {
        add => _events.WebResourceRequested += value;
        remove => _events.WebResourceRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested
    {
        add => _events.EnvironmentRequested += value;
        remove => _events.EnvironmentRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested
    {
        add => _events.DownloadRequested += value;
        remove => _events.DownloadRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested
    {
        add => _events.PermissionRequested += value;
        remove => _events.PermissionRequested -= value;
    }

    /// <inheritdoc />
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated
    {
        add => _events.AdapterCreated += value;
        remove => _events.AdapterCreated -= value;
    }

    /// <inheritdoc />
    public event EventHandler? AdapterDestroyed
    {
        add => _events.AdapterDestroyed += value;
        remove => _events.AdapterDestroyed -= value;
    }

    /// <summary>Raised when the zoom factor changes.</summary>
    public event EventHandler<double>? ZoomFactorChanged
    {
        add => _events.ZoomFactorChanged += value;
        remove => _events.ZoomFactorChanged -= value;
    }

    /// <summary>Raised when the user triggers a context menu (right-click, long-press).</summary>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add => _events.ContextMenuRequested += value;
        remove => _events.ContextMenuRequested -= value;
    }

    /// <summary>Raised when a drag operation enters the WebView bounds.</summary>
    public event EventHandler<DragEventArgs>? DragEntered
    {
        add => _events.DragEntered += value;
        remove => _events.DragEntered -= value;
    }

    /// <summary>Raised when a drag operation moves over the WebView.</summary>
    public event EventHandler<DragEventArgs>? DragOver
    {
        add => _events.DragOver += value;
        remove => _events.DragOver -= value;
    }

    /// <summary>Raised when a drag operation leaves the WebView bounds.</summary>
    public event EventHandler<EventArgs>? DragLeft
    {
        add => _events.DragLeft += value;
        remove => _events.DragLeft -= value;
    }

    /// <summary>Raised when a drop is completed on the WebView.</summary>
    public event EventHandler<DropEventArgs>? DropCompleted
    {
        add => _events.DropCompleted += value;
        remove => _events.DropCompleted -= value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Raise AdapterDestroyed before flipping the disposed latch so that observers still see a
        // "live" core during the callback; MarkAdapterDestroyedOnce itself is idempotent.
        if (_lifecycle.IsDisposed)
        {
            return;
        }

        _logger.LogDebug("Dispose: begin");
        RaiseAdapterDestroyedOnce();
        _lifecycle.TryTransitionToDisposed();

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
        // _adapterEventRuntime / _navigationRuntime hold only injected references (and
        // NavigationRuntime's active-op is already faulted above); their own types document why
        // Dispose() is absent. Do not add "defensive" disposal here — it would invert ownership.

        _logger.LogDebug("Dispose: completed");
    }

    /// <inheritdoc />
    public Task NavigateAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _logger.LogDebug("NavigateAsync: {Uri}", uri);

        return _operationQueue.EnqueueAsync(nameof(NavigateAsync), () => _navigationRuntime.StartNavigationRequestCoreAsync(
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

        return _operationQueue.EnqueueAsync(nameof(NavigateToStringAsync), () => _navigationRuntime.StartNavigationRequestCoreAsync(
            requestUri: requestUri,
            adapterInvoke: navigationId => _adapter.NavigateToStringAsync(navigationId, html, baseUrl))).Unwrap();
    }

    /// <inheritdoc />
    public Task<string?> InvokeScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        _logger.LogDebug("InvokeScriptAsync: script length={Length}", script.Length);

        if (_lifecycle.IsDisposed)
        {
            return Task.FromException<string?>(new ObjectDisposedException(nameof(WebViewCore)));
        }

        return _operationQueue.EnqueueAsync(nameof(InvokeScriptAsync), () => InvokeScriptOnUiThreadAsync(script));

        async Task<string?> InvokeScriptOnUiThreadAsync(string s)
        {
            _context.ThrowIfDisposed();

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
        => _operationQueue.EnqueueAsync(nameof(GoBackAsync), () => Task.FromResult(GoBackCore()));

    private bool GoBackCore()
    {
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(GoBackAsync));

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
        => _operationQueue.EnqueueAsync(nameof(GoForwardAsync), () => Task.FromResult(GoForwardCore()));

    private bool GoForwardCore()
    {
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(GoForwardAsync));

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
        => _operationQueue.EnqueueAsync(nameof(RefreshAsync), () => Task.FromResult(RefreshCore()));

    private bool RefreshCore()
    {
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(RefreshAsync));

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
        => _operationQueue.EnqueueAsync(nameof(StopAsync), () => Task.FromResult(StopCore()));

    private bool StopCore()
    {
        _context.ThrowIfDisposed();
        _context.ThrowIfNotOnUiThread(nameof(StopAsync));

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
    public ICookieManager? TryGetCookieManager() => _cookieManager;

    /// <inheritdoc />
    public ICommandManager? TryGetCommandManager() => _commandManager;

    /// <inheritdoc />
    public IWebViewRpcService? Rpc => _bridgeRuntime.Rpc;

    // ==================== DevTools ====================

    /// <inheritdoc />
    public Task OpenDevToolsAsync() => _featureRuntime.OpenDevToolsAsync();

    /// <inheritdoc />
    public Task CloseDevToolsAsync() => _featureRuntime.CloseDevToolsAsync();

    /// <inheritdoc />
    public Task<bool> IsDevToolsOpenAsync() => _featureRuntime.IsDevToolsOpenAsync();

    // ==================== Bridge ====================

    /// <inheritdoc />
    public IBridgeTracer? BridgeTracer
    {
        get => _bridgeRuntime.BridgeTracer;
        set => _bridgeRuntime.BridgeTracer = value;
    }

    /// <inheritdoc />
    public IBridgeService Bridge => _bridgeRuntime.Bridge;

    /// <inheritdoc />
    public Task<byte[]> CaptureScreenshotAsync() => _featureRuntime.CaptureScreenshotAsync();

    /// <inheritdoc />
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => _featureRuntime.PrintToPdfAsync(options);

    // ==================== Zoom ====================

    /// <summary>
    /// Gets or sets the zoom factor (1.0 = 100%). Clamped to [0.25, 5.0].
    /// Returns 1.0 if the adapter does not support zoom.
    /// </summary>
    public Task<double> GetZoomFactorAsync() => _featureRuntime.GetZoomFactorAsync();

    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor) => _featureRuntime.SetZoomFactorAsync(zoomFactor);

    /// <summary>
    /// Searches the current page for the given text.
    /// </summary>
    /// <param name="text">The search text. Must not be null or empty.</param>
    /// <param name="options">Optional search options (case sensitivity, direction).</param>
    /// <returns>A <see cref="FindInPageEventArgs"/> with match count and active index.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or empty.</exception>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
        => _featureRuntime.FindInPageAsync(text, options);

    /// <summary>
    /// Clears find-in-page highlights and resets search state.
    /// </summary>
    /// <param name="clearHighlights">Whether to remove visual highlights. Default: true.</param>
    public Task StopFindInPageAsync(bool clearHighlights = true)
        => _featureRuntime.StopFindInPageAsync(clearHighlights);

    /// <summary>
    /// Registers a JavaScript snippet to run at document start on every page load.
    /// </summary>
    /// <param name="javaScript">The script to inject.</param>
    /// <returns>An opaque script ID that can be passed to <see cref="RemovePreloadScriptAsync"/>.</returns>
    public Task<string> AddPreloadScriptAsync(string javaScript)
        => _featureRuntime.AddPreloadScriptAsync(javaScript);

    /// <summary>
    /// Removes a previously registered preload script by its ID.
    /// </summary>
    /// <param name="scriptId">The ID returned by <see cref="AddPreloadScriptAsync"/>.</param>
    public Task RemovePreloadScriptAsync(string scriptId)
        => _featureRuntime.RemovePreloadScriptAsync(scriptId);

    /// <summary>
    /// Asynchronously retrieves the native platform WebView handle.
    /// This is the primary any-thread API surface and always marshals adapter access to the UI thread.
    /// </summary>
    public Task<INativeHandle?> TryGetWebViewHandleAsync()
        => _featureRuntime.TryGetWebViewHandleAsync();

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
    public void SetCustomUserAgent(string? userAgent) => _featureRuntime.SetCustomUserAgent(userAgent);

    /// <inheritdoc />
    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
        => _bridgeRuntime.EnableWebMessageBridge(options);

    /// <inheritdoc />
    public void DisableWebMessageBridge()
        => _bridgeRuntime.DisableWebMessageBridge();

    /// <summary>
    /// Re-injects the base RPC JS stub and all exposed service stubs when the bridge is enabled.
    /// Called after successful navigation to restore <c>window.agWebView</c> in the new JS context.
    /// </summary>
    internal void ReinjectBridgeStubsIfEnabled()
        => _bridgeRuntime.ReinjectBridgeStubsIfEnabled();

    // ==================== SPA Hosting ====================

    /// <summary>
    /// Enables SPA hosting. Registers the custom scheme, subscribes to WebResourceRequested,
    /// and optionally auto-enables the bridge.
    /// </summary>
    public void EnableSpaHosting(SpaHostingOptions options)
        => _spaHostingRuntime.EnableSpaHosting(options);

    private void RaiseAdapterDestroyedOnce()
        => _lifecycle.MarkAdapterDestroyedOnce(() =>
        {
            _logger.LogDebug("AdapterDestroyed: raising");
            _events.RaiseAdapterDestroyed();
        });
}
