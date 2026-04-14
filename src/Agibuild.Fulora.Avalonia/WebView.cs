using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora;

/// <summary>
/// An Avalonia control that embeds a platform-native WebView.
/// <para>
/// Usage in XAML:
/// <code>&lt;agw:WebView Source="https://example.com" /&gt;</code>
/// </para>
/// </summary>
public class WebView : NativeControlHost, ISpaHostingWebView
{
    private static readonly Uri AboutBlankUri = new("about:blank");

    // ---------------------------------------------------------------------------
    //  Avalonia StyledProperties
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Styled property backing <see cref="Source"/>.
    /// </summary>
    public static readonly StyledProperty<Uri?> SourceProperty =
        AvaloniaProperty.Register<WebView, Uri?>(nameof(Source));

    /// <summary>
    /// Direct property backing <see cref="CanGoBack"/>.
    /// </summary>
    public static readonly DirectProperty<WebView, bool> CanGoBackProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(CanGoBack), o => o.CanGoBack);

    /// <summary>
    /// Direct property backing <see cref="CanGoForward"/>.
    /// </summary>
    public static readonly DirectProperty<WebView, bool> CanGoForwardProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(CanGoForward), o => o.CanGoForward);

    /// <summary>
    /// Direct property backing <see cref="IsLoading"/>.
    /// </summary>
    public static readonly DirectProperty<WebView, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(IsLoading), o => o.IsLoading);

    /// <summary>
    /// Styled property backing <see cref="ZoomFactor"/>.
    /// </summary>
    public static readonly StyledProperty<double> ZoomFactorProperty =
        AvaloniaProperty.Register<WebView, double>(nameof(ZoomFactor), defaultValue: 1.0);

    /// <summary>
    /// Styled property backing <see cref="OverlayContent"/>.
    /// When set, creates a companion overlay for rendering Avalonia controls above the WebView.
    /// </summary>
    public static readonly StyledProperty<object?> OverlayContentProperty =
        AvaloniaProperty.Register<WebView, object?>(nameof(OverlayContent));

    // ---------------------------------------------------------------------------
    //  Internal state
    // ---------------------------------------------------------------------------

    private WebViewCore? _core;
    private bool _coreAttached;
    private bool _adapterUnavailable;
    private ILoggerFactory? _loggerFactory;
    private EventHandler<ContextMenuRequestedEventArgs>? _contextMenuRequestedHandlers;
    private EventHandler<DragEventArgs>? _dragEnteredHandlers;
    private EventHandler<DragEventArgs>? _dragOverHandlers;
    private EventHandler<EventArgs>? _dragLeftHandlers;
    private EventHandler<DropEventArgs>? _dropCompletedHandlers;
    private Window? _hostWindow;
    private EventHandler<WindowClosingEventArgs>? _hostWindowClosingHandler;
    private WebViewOverlayHost? _overlayHost;
    private EventHandler? _layoutUpdatedHandler;
    private EventHandler<PixelPointEventArgs>? _hostWindowPositionChangedHandler;
    private readonly WebViewControlRuntime _controlRuntime = new();
    private readonly WebViewControlEventRuntime _eventRuntime;

    // ---------------------------------------------------------------------------
    //  Constructor
    // ---------------------------------------------------------------------------

    static WebView()
    {
        SourceProperty.Changed.AddClassHandler<WebView>((wv, e) => wv.OnSourceChanged(e));
        ZoomFactorProperty.Changed.AddClassHandler<WebView>((wv, e) => wv.OnZoomFactorChanged(e));
        OverlayContentProperty.Changed.AddClassHandler<WebView>((wv, e) => wv.OnOverlayContentChanged(e));
    }

    /// <summary>
    /// Creates a new <see cref="WebView"/> control shell.
    /// </summary>
    public WebView()
    {
        _eventRuntime = new WebViewControlEventRuntime(
            raiseNavigationStarted: args => OnCoreNavigationStarted(args),
            raiseNavigationCompleted: args => OnCoreNavigationCompleted(args),
            raiseNewWindowRequested: args => OnCoreNewWindowRequested(args),
            raiseWebMessageReceived: args => WebMessageReceived?.Invoke(this, args),
            raiseWebResourceRequested: args => WebResourceRequested?.Invoke(this, args),
            raiseEnvironmentRequested: args => EnvironmentRequested?.Invoke(this, args),
            raiseDownloadRequested: args => DownloadRequested?.Invoke(this, args),
            raisePermissionRequested: args => PermissionRequested?.Invoke(this, args),
            raiseAdapterCreated: args => AdapterCreated?.Invoke(this, args),
            raiseAdapterDestroyed: () => AdapterDestroyed?.Invoke(this, EventArgs.Empty),
            raiseZoomFactorChanged: newZoom => OnCoreZoomFactorChanged(newZoom),
            getContextMenuHandlers: () => _contextMenuRequestedHandlers,
            getDragEnteredHandlers: () => _dragEnteredHandlers,
            getDragOverHandlers: () => _dragOverHandlers,
            getDragLeftHandlers: () => _dragLeftHandlers,
            getDropCompletedHandlers: () => _dropCompletedHandlers,
            navigateInPlaceAsync: uri => _controlRuntime.NavigateAsync(uri),
            getInitialZoomFactor: () => ZoomFactor,
            applyInitialZoomFactor: zoom => _ = _controlRuntime.SetZoomFactorAsync(zoom));
    }

    // ---------------------------------------------------------------------------
    //  Public surface (mirrors IWebView)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the <see cref="ILoggerFactory"/> used to create loggers for internal diagnostics.
    /// Set this before the control is attached to the visual tree for full coverage.
    /// When <c>null</c>, logging is disabled (NullLogger).
    /// </summary>
    public ILoggerFactory? LoggerFactory
    {
        get => _loggerFactory;
        set => _loggerFactory = value;
    }

    /// <summary>
    /// Optional per-instance environment options. When set, these options are used for this control
    /// instead of the global <see cref="WebViewEnvironment.Options"/>.
    /// Set this before the control is attached to the visual tree.
    /// </summary>
    public IWebViewEnvironmentOptions? EnvironmentOptions { get; set; }

    /// <summary>
    /// Gets or sets the current navigation URI. Setting this triggers a navigation.
    /// </summary>
    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    // IWebViewNavigation requires non-null Uri; the control surface remains nullable for XAML binding.
    Uri IWebViewNavigation.Source
    {
        get => Source ?? AboutBlankUri;
        set => Source = value;
    }

    /// <inheritdoc />
    public bool CanGoBack => _core?.CanGoBack ?? false;

    /// <inheritdoc />
    public bool CanGoForward => _core?.CanGoForward ?? false;

    /// <summary>
    /// <c>true</c> while a navigation is in progress.
    /// </summary>
    public bool IsLoading => _core?.IsLoading ?? false;

    /// <summary>
    /// <c>true</c> when a platform adapter is available and the WebView is functional.
    /// <c>false</c> on platforms without a registered adapter (e.g. Android before the adapter is implemented).
    /// </summary>
    public bool IsAvailable => _core is not null && _coreAttached;

    /// <summary>
    /// The channel id for web message bridge isolation.
    /// Only valid after the control is attached to the visual tree.
    /// </summary>
    public Guid ChannelId => _core?.ChannelId ?? Guid.Empty;

    /// <summary>
    /// Gets or sets the zoom factor (1.0 = 100%). Clamped to [0.25, 5.0].
    /// Bindable via <see cref="ZoomFactorProperty"/>.
    /// </summary>
    public double ZoomFactor
    {
        get => GetValue(ZoomFactorProperty);
        set => SetValue(ZoomFactorProperty, value);
    }

    /// <summary>
    /// Gets or sets the overlay content for rendering Avalonia controls above the WebView.
    /// When set, a companion overlay host is created; when cleared, it is disposed.
    /// Bindable via <see cref="OverlayContentProperty"/>.
    /// </summary>
    public object? OverlayContent
    {
        get => GetValue(OverlayContentProperty);
        set => SetValue(OverlayContentProperty, value);
    }

    /// <inheritdoc />
    public Task<double> GetZoomFactorAsync()
    {
        if (_core is null)
            throw new InvalidOperationException("WebView is not initialized.");
        return _core.GetZoomFactorAsync();
    }

    /// <inheritdoc />
    public Task SetZoomFactorAsync(double zoomFactor)
    {
        if (_core is null)
            throw new InvalidOperationException("WebView is not initialized.");
        return _core.SetZoomFactorAsync(zoomFactor);
    }

    /// <summary>Raised when the zoom factor changes.</summary>
    public event EventHandler<double>? ZoomFactorChanged;

    // --- Navigation ---

    /// <inheritdoc />
    public Task NavigateAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureCore();
        return _core!.NavigateAsync(uri);
    }

    /// <inheritdoc />
    public Task NavigateToStringAsync(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        EnsureCore();
        return _core!.NavigateToStringAsync(html);
    }

    /// <inheritdoc />
    public Task NavigateToStringAsync(string html, Uri? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        EnsureCore();
        return _core!.NavigateToStringAsync(html, baseUrl);
    }

    /// <summary>
    /// Returns a cookie manager if the underlying adapter supports it; otherwise <c>null</c>.
    /// </summary>
    public ICookieManager? TryGetCookieManager()
    {
        return _controlRuntime.TryGetCookieManager();
    }

    /// <summary>
    /// Returns a command manager if the underlying adapter supports it; otherwise <c>null</c>.
    /// </summary>
    public ICommandManager? TryGetCommandManager()
    {
        return _controlRuntime.TryGetCommandManager();
    }

    /// <summary>
    /// Gets the RPC service for bidirectional JS ↔ C# method calls.
    /// Returns <c>null</c> until the WebMessage bridge is enabled.
    /// </summary>
    public IWebViewRpcService? Rpc => _controlRuntime.Rpc;

    /// <summary>
    /// Sets the bridge tracer for observability. Must be set before the first access to
    /// <see cref="Bridge"/>; changes after the bridge is created are silently ignored.
    /// </summary>
    public IBridgeTracer? BridgeTracer
    {
        get => _controlRuntime.BridgeTracer;
        set => _controlRuntime.BridgeTracer = value;
    }

    /// <summary>
    /// Gets the type-safe bridge service for exposing C# services to JS (<see cref="JsExportAttribute"/>)
    /// and importing JS services into C# (<see cref="JsImportAttribute"/>).
    /// Accessing this property auto-enables the WebMessage bridge if not already enabled.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the control has not been attached yet.</exception>
    public IBridgeService Bridge => _controlRuntime.Bridge;

    /// <summary>
    /// Opens the browser developer tools (inspector) at runtime.
    /// No-op if the platform adapter does not support runtime DevTools toggling.
    /// </summary>
    public Task OpenDevToolsAsync()
    {
        return _controlRuntime.OpenDevToolsAsync();
    }

    /// <summary>
    /// Closes the browser developer tools.
    /// No-op if the platform adapter does not support runtime DevTools toggling.
    /// </summary>
    public Task CloseDevToolsAsync()
    {
        return _controlRuntime.CloseDevToolsAsync();
    }

    /// <summary>
    /// Returns whether developer tools are currently open.
    /// Always returns false if the platform adapter does not support this check.
    /// </summary>
    public Task<bool> IsDevToolsOpenAsync()
    {
        return _controlRuntime.IsDevToolsOpenAsync();
    }

    /// <summary>
    /// Captures a screenshot of the current viewport as a PNG byte array.
    /// Throws <see cref="NotSupportedException"/> if the adapter does not support screenshots.
    /// </summary>
    public Task<byte[]> CaptureScreenshotAsync()
    {
        return _controlRuntime.CaptureScreenshotAsync();
    }

    /// <summary>
    /// Prints the current page to a PDF byte array.
    /// Throws <see cref="NotSupportedException"/> if the adapter does not support printing.
    /// </summary>
    public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null)
    {
        return _controlRuntime.PrintToPdfAsync(options);
    }

    /// <summary>
    /// Searches the current page for the given text.
    /// </summary>
    public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
    {
        return _controlRuntime.FindInPageAsync(text, options);
    }

    /// <summary>
    /// Clears find-in-page highlights and resets search state.
    /// </summary>
    public Task StopFindInPageAsync(bool clearHighlights = true)
    {
        return _controlRuntime.StopFindInPageAsync(clearHighlights);
    }

    /// <summary>
    /// Registers a JavaScript snippet to run at document start on every page load.
    /// </summary>
    public Task<string> AddPreloadScriptAsync(string javaScript)
    {
        return _controlRuntime.AddPreloadScriptAsync(javaScript);
    }

    /// <summary>
    /// Removes a previously registered preload script by its ID.
    /// </summary>
    public Task RemovePreloadScriptAsync(string scriptId)
    {
        return _controlRuntime.RemovePreloadScriptAsync(scriptId);
    }

    /// <summary>
    /// Raised when the user triggers a context menu (right-click, long-press).
    /// Set <c>Handled = true</c> in the event args to suppress the native context menu.
    /// </summary>
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested
    {
        add
        {
            _contextMenuRequestedHandlers += value;
            if (_core is not null)
            {
                _core.ContextMenuRequested += value;
            }
        }
        remove
        {
            _contextMenuRequestedHandlers -= value;
            if (_core is not null)
            {
                _core.ContextMenuRequested -= value;
            }
        }
    }

    /// <summary>Raised when a drag operation enters the WebView bounds.</summary>
    public event EventHandler<DragEventArgs>? DragEntered
    {
        add
        {
            _dragEnteredHandlers += value;
            if (_core is not null)
            {
                _core.DragEntered += value;
            }
        }
        remove
        {
            _dragEnteredHandlers -= value;
            if (_core is not null)
            {
                _core.DragEntered -= value;
            }
        }
    }

    /// <summary>Raised when a drag operation moves over the WebView.</summary>
    public event EventHandler<DragEventArgs>? DragOver
    {
        add
        {
            _dragOverHandlers += value;
            if (_core is not null)
            {
                _core.DragOver += value;
            }
        }
        remove
        {
            _dragOverHandlers -= value;
            if (_core is not null)
            {
                _core.DragOver -= value;
            }
        }
    }

    /// <summary>Raised when a drag operation leaves the WebView bounds.</summary>
    public event EventHandler<EventArgs>? DragLeft
    {
        add
        {
            _dragLeftHandlers += value;
            if (_core is not null)
            {
                _core.DragLeft += value;
            }
        }
        remove
        {
            _dragLeftHandlers -= value;
            if (_core is not null)
            {
                _core.DragLeft -= value;
            }
        }
    }

    /// <summary>Raised when a drop operation completes on the WebView.</summary>
    public event EventHandler<DropEventArgs>? DropCompleted
    {
        add
        {
            _dropCompletedHandlers += value;
            if (_core is not null)
            {
                _core.DropCompleted += value;
            }
        }
        remove
        {
            _dropCompletedHandlers -= value;
            if (_core is not null)
            {
                _core.DropCompleted -= value;
            }
        }
    }

    /// <summary>
    /// Returns the underlying platform WebView handle, or <c>null</c> if not available.
    /// </summary>
    public INativeHandle? TryGetWebViewHandle()
    {
        return _controlRuntime.TryGetWebViewHandle();
    }

    /// <summary>
    /// Returns the underlying platform WebView handle asynchronously, or <c>null</c> if not available.
    /// </summary>
    public Task<INativeHandle?> TryGetWebViewHandleAsync()
    {
        return _controlRuntime.TryGetWebViewHandleAsync();
    }

    /// <summary>
    /// Sets the custom User-Agent string at runtime.
    /// Pass <c>null</c> to revert to the platform default.
    /// </summary>
    public void SetCustomUserAgent(string? userAgent)
    {
        _controlRuntime.SetCustomUserAgent(userAgent);
    }

    /// <summary>
    /// Enables the WebMessage bridge with the specified policy options.
    /// After this call, incoming <c>WebMessageReceived</c> events are filtered by origin, protocol, and channel.
    /// Must be called on the UI thread after the control is attached.
    /// </summary>
    public void EnableWebMessageBridge(WebMessageBridgeOptions options)
    {
        _controlRuntime.EnableWebMessageBridge(options);
    }

    /// <summary>
    /// Disables the WebMessage bridge. Subsequent incoming web messages will be silently dropped.
    /// Must be called on the UI thread.
    /// </summary>
    public void DisableWebMessageBridge()
    {
        _controlRuntime.DisableWebMessageBridge();
    }

    /// <summary>
    /// Enables SPA hosting. Registers the custom scheme, subscribes to WebResourceRequested,
    /// and optionally auto-enables the bridge.
    /// </summary>
    public void EnableSpaHosting(SpaHostingOptions options)
    {
        _controlRuntime.EnableSpaHosting(options);
    }

    /// <inheritdoc />
    public Task<string?> InvokeScriptAsync(string script)
    {
        return _controlRuntime.InvokeScriptAsync(script);
    }

    /// <inheritdoc />
    public Task<bool> GoBackAsync()
    {
        return _controlRuntime.GoBackAsync();
    }

    /// <inheritdoc />
    public Task<bool> GoForwardAsync()
    {
        return _controlRuntime.GoForwardAsync();
    }

    /// <inheritdoc />
    public Task<bool> RefreshAsync()
    {
        return _controlRuntime.RefreshAsync();
    }

    /// <inheritdoc />
    public Task<bool> StopAsync()
    {
        return _controlRuntime.StopAsync();
    }

    // --- Events (bubbled from WebViewCore) ---

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

    // ---------------------------------------------------------------------------
    //  NativeControlHost lifecycle
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Hooks host window closing events when attached to the visual tree.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookHostWindowClosing();
        SubscribeLayoutOverlayEvents();
    }

    /// <summary>
    /// Unhooks host window closing events when detached from the visual tree.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeLayoutOverlayEvents();
        UnhookHostWindowClosing();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Creates and attaches the underlying native WebView host.
    /// </summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);

        try
        {
            var dispatcher = new SynchronizationContextWebViewDispatcher();
            var effectiveLoggerFactory = _loggerFactory ?? WebViewEnvironment.LoggerFactory;
            var logger = effectiveLoggerFactory?.CreateLogger<WebViewCore>()
                         ?? (ILogger<WebViewCore>)NullLogger<WebViewCore>.Instance;

            _core = WebViewCore.CreateForControl(dispatcher, logger, EnvironmentOptions);
            _controlRuntime.AttachCore(_core);

            // Subscribe before Attach so we receive AdapterCreated raised during Attach().
            _eventRuntime.Attach(_core);

            _core.Attach(new AvaloniaNativeHandleAdapter(handle));
            _coreAttached = true;
            HookHostWindowClosing();

            // If Source was set before attachment, navigate now (after AdapterCreated).
            var pendingSource = Source;
            if (pendingSource is not null)
            {
                _ = _core.NavigateAsync(pendingSource);
            }
        }
        catch (PlatformNotSupportedException)
        {
            // No adapter for this platform — degrade gracefully (empty control).
            _core?.Dispose();
            _core = null;
            _coreAttached = false;
            _adapterUnavailable = true;
            _controlRuntime.MarkAdapterUnavailable();
        }
        catch
        {
            _core?.Dispose();
            _core = null;
            _coreAttached = false;
            _controlRuntime.ClearCore();
            throw;
        }

        return handle;
    }

    /// <summary>
    /// Detaches and disposes the underlying native WebView host.
    /// </summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        UnhookHostWindowClosing();
        _eventRuntime.Detach(_core);

        _overlayHost?.Dispose();
        _overlayHost = null;

        if (_coreAttached)
        {
            _core?.Detach();
            _coreAttached = false;
        }

        _core?.Dispose();
        _core = null;
        _controlRuntime.ClearCore();

        base.DestroyNativeControlCore(control);
    }

    // ---------------------------------------------------------------------------
    //  Private helpers
    // ---------------------------------------------------------------------------

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_core is null || !_coreAttached)
        {
            return;
        }

        if (e.NewValue is Uri newUri)
        {
            _ = _core.NavigateAsync(newUri);
        }
    }

    private void OnZoomFactorChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_core is null || !_coreAttached) return;
        if (e.NewValue is double newZoom)
        {
            _ = _core.SetZoomFactorAsync(newZoom);
        }
    }

    private void OnOverlayContentChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var newContent = e.NewValue;

        if (newContent is not null)
        {
            _overlayHost ??= new WebViewOverlayHost(this);
            _overlayHost.Content = newContent;

            // Trigger immediate position update if already in the visual tree.
            if (VisualRoot is not null)
                OnLayoutUpdatedForOverlay(this, EventArgs.Empty);
        }
        else
        {
            _overlayHost?.Dispose();
            _overlayHost = null;
        }
    }

    private void EnsureCore()
    {
        if (_controlRuntime.Core is null)
        {
            if (_adapterUnavailable)
            {
                throw new PlatformNotSupportedException(
                    "No WebView adapter is available for the current platform. " +
                    "WebView functionality is not supported.");
            }

            throw new InvalidOperationException(
                "WebView is not yet attached to the visual tree. " +
                "Wait until the control is loaded before calling navigation methods.");
        }
    }

    // Internal test seam for lifecycle wiring assertions without private reflection.
    internal void TestOnlyAttachCore(WebViewCore core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
        _controlRuntime.AttachCore(core);
        _adapterUnavailable = false;
    }

    internal void TestOnlySubscribeCoreEvents()
    {
        if (_core is not null)
            _eventRuntime.Attach(_core);
    }

    internal void TestOnlyUnsubscribeCoreEvents() => _eventRuntime.Detach(_core);

    private void OnCoreNavigationStarted(NavigationStartingEventArgs e)
    {
        NavigationStarted?.Invoke(this, e);
        RaisePropertyChanged(IsLoadingProperty, false, true);
    }

    private void OnCoreNavigationCompleted(NavigationCompletedEventArgs e)
    {
        NavigationCompleted?.Invoke(this, e);
        RaisePropertyChanged(IsLoadingProperty, true, false);
        RaisePropertyChanged(CanGoBackProperty, !CanGoBack, CanGoBack);
        RaisePropertyChanged(CanGoForwardProperty, !CanGoForward, CanGoForward);
    }

    private void OnCoreNewWindowRequested(NewWindowRequestedEventArgs e)
        => NewWindowRequested?.Invoke(this, e);

    private void OnCoreZoomFactorChanged(double newZoom)
    {
        SetCurrentValue(ZoomFactorProperty, newZoom);
        ZoomFactorChanged?.Invoke(this, newZoom);
    }

    /// <summary>
    /// Releases the native WebView resources and detaches from the host window lifecycle.
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        UnhookHostWindowClosing();
        _eventRuntime.Detach(_core);

        _overlayHost?.Dispose();
        _overlayHost = null;

        if (_coreAttached)
        {
            _core?.Detach();
            _coreAttached = false;
        }

        _core?.Dispose();
        _core = null;
    }

    // Close events can be user-triggered, app-triggered, or OS-triggered.
    // For Chromium/WebView2 teardown safety, all close paths should detach early.
    internal static bool ShouldDetachForHostWindowClosing(bool isProgrammatic, WindowCloseReason closeReason)
        => true;

    internal bool HandleHostWindowClosing(bool isProgrammatic, WindowCloseReason closeReason)
    {
        if (!ShouldDetachForHostWindowClosing(isProgrammatic, closeReason))
        {
            return false;
        }

        if (!_coreAttached)
        {
            return false;
        }

        try
        {
            _core?.Detach();
            _coreAttached = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void HookHostWindowClosing()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (ReferenceEquals(window, _hostWindow))
            return;

        UnhookHostWindowClosing();

        if (window is null)
            return;

        _hostWindow = window;
        _hostWindowClosingHandler = (_, e) =>
        {
            _ = HandleHostWindowClosing(e.IsProgrammatic, e.CloseReason);
        };
        _hostWindow.Closing += _hostWindowClosingHandler;
    }

    private void UnhookHostWindowClosing()
    {
        if (_hostWindow is not null && _hostWindowClosingHandler is not null)
        {
            _hostWindow.Closing -= _hostWindowClosingHandler;
        }

        _hostWindow = null;
        _hostWindowClosingHandler = null;
    }

    private void SubscribeLayoutOverlayEvents()
    {
        if (_layoutUpdatedHandler is not null)
            return;

        _layoutUpdatedHandler = OnLayoutUpdatedForOverlay;
        LayoutUpdated += _layoutUpdatedHandler;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null && _hostWindowPositionChangedHandler is null)
        {
            _hostWindowPositionChangedHandler = (_, _) => OnLayoutUpdatedForOverlay(this, EventArgs.Empty);
            window.PositionChanged += _hostWindowPositionChangedHandler;
        }
    }

    private void UnsubscribeLayoutOverlayEvents()
    {
        if (_layoutUpdatedHandler is not null)
        {
            LayoutUpdated -= _layoutUpdatedHandler;
            _layoutUpdatedHandler = null;
        }

        if (_hostWindowPositionChangedHandler is not null)
        {
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window is not null)
                window.PositionChanged -= _hostWindowPositionChangedHandler;
            _hostWindowPositionChangedHandler = null;
        }
    }

    private void OnLayoutUpdatedForOverlay(object? sender, EventArgs e)
    {
        if (_overlayHost is null)
            return;

        var topLevel = VisualRoot as TopLevel;
        var screenOffset = topLevel is not null
            ? (this.TranslatePoint(new Point(0, 0), topLevel) ?? default)
            : default;
        var dpiScale = topLevel?.RenderScaling ?? 1.0;

        _overlayHost.UpdatePosition(Bounds, screenOffset, dpiScale);
        _overlayHost.SyncVisibilityWith(IsVisible);
    }

    private sealed class AvaloniaNativeHandleAdapter : INativeHandle
    {
        private readonly IPlatformHandle _inner;

        public AvaloniaNativeHandleAdapter(IPlatformHandle inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public nint Handle => _inner.Handle;

        public string HandleDescriptor => _inner.HandleDescriptor ?? string.Empty;
    }
}
