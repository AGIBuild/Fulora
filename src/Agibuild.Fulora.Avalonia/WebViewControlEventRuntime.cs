namespace Agibuild.Fulora;

internal sealed class WebViewControlEventRuntime
{
    private readonly Action<NavigationStartingEventArgs> _raiseNavigationStarted;
    private readonly Action<NavigationCompletedEventArgs> _raiseNavigationCompleted;
    private readonly Action<NewWindowRequestedEventArgs> _raiseNewWindowRequested;
    private readonly Action<WebMessageReceivedEventArgs> _raiseWebMessageReceived;
    private readonly Action<WebResourceRequestedEventArgs> _raiseWebResourceRequested;
    private readonly Action<EnvironmentRequestedEventArgs> _raiseEnvironmentRequested;
    private readonly Action<DownloadRequestedEventArgs> _raiseDownloadRequested;
    private readonly Action<PermissionRequestedEventArgs> _raisePermissionRequested;
    private readonly Action<AdapterCreatedEventArgs> _raiseAdapterCreated;
    private readonly Action _raiseAdapterDestroyed;
    private readonly Action<double> _raiseZoomFactorChanged;
    private readonly Func<EventHandler<ContextMenuRequestedEventArgs>?> _getContextMenuHandlers;
    private readonly Func<EventHandler<DragEventArgs>?> _getDragEnteredHandlers;
    private readonly Func<EventHandler<DragEventArgs>?> _getDragOverHandlers;
    private readonly Func<EventHandler<EventArgs>?> _getDragLeftHandlers;
    private readonly Func<EventHandler<DropEventArgs>?> _getDropCompletedHandlers;
    private readonly EventHandler<ContextMenuRequestedEventArgs> _contextMenuWrapper;
    private readonly EventHandler<DragEventArgs> _dragEnteredWrapper;
    private readonly EventHandler<DragEventArgs> _dragOverWrapper;
    private readonly EventHandler<EventArgs> _dragLeftWrapper;
    private readonly EventHandler<DropEventArgs> _dropCompletedWrapper;
    private readonly Func<Uri, Task> _navigateInPlaceAsync;
    private readonly Func<double> _getInitialZoomFactor;
    private readonly Action<double> _applyInitialZoomFactor;

    private EventHandler<NavigationStartingEventArgs>? _navigationStarted;
    private EventHandler<NavigationCompletedEventArgs>? _navigationCompleted;
    private EventHandler<NewWindowRequestedEventArgs>? _newWindowRequested;
    private EventHandler<WebMessageReceivedEventArgs>? _webMessageReceived;
    private EventHandler<WebResourceRequestedEventArgs>? _webResourceRequested;
    private EventHandler<EnvironmentRequestedEventArgs>? _environmentRequested;
    private EventHandler<DownloadRequestedEventArgs>? _downloadRequested;
    private EventHandler<PermissionRequestedEventArgs>? _permissionRequested;
    private EventHandler<AdapterCreatedEventArgs>? _adapterCreated;
    private EventHandler? _adapterDestroyed;
    private EventHandler<double>? _zoomFactorChanged;
    private IWebViewCoreControlEvents? _attachedCore;

    public WebViewControlEventRuntime(
        WebViewControlEventCallbacks callbacks,
        WebViewControlInteractionAccessors interactionHandlers,
        WebViewControlNavigationHooks navigationHooks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(interactionHandlers);
        ArgumentNullException.ThrowIfNull(navigationHooks);

        _raiseNavigationStarted = callbacks.RaiseNavigationStarted;
        _raiseNavigationCompleted = callbacks.RaiseNavigationCompleted;
        _raiseNewWindowRequested = callbacks.RaiseNewWindowRequested;
        _raiseWebMessageReceived = callbacks.RaiseWebMessageReceived;
        _raiseWebResourceRequested = callbacks.RaiseWebResourceRequested;
        _raiseEnvironmentRequested = callbacks.RaiseEnvironmentRequested;
        _raiseDownloadRequested = callbacks.RaiseDownloadRequested;
        _raisePermissionRequested = callbacks.RaisePermissionRequested;
        _raiseAdapterCreated = callbacks.RaiseAdapterCreated;
        _raiseAdapterDestroyed = callbacks.RaiseAdapterDestroyed;
        _raiseZoomFactorChanged = callbacks.RaiseZoomFactorChanged;
        _getContextMenuHandlers = interactionHandlers.GetContextMenuHandlers;
        _getDragEnteredHandlers = interactionHandlers.GetDragEnteredHandlers;
        _getDragOverHandlers = interactionHandlers.GetDragOverHandlers;
        _getDragLeftHandlers = interactionHandlers.GetDragLeftHandlers;
        _getDropCompletedHandlers = interactionHandlers.GetDropCompletedHandlers;

        // Stable wrappers read the current aggregate on every invocation, so
        // handlers added or removed between Attach and Detach are always respected
        // without relying on a snapshot captured at Attach time.
        _contextMenuWrapper = (s, e) => _getContextMenuHandlers()?.Invoke(s, e);
        _dragEnteredWrapper = (s, e) => _getDragEnteredHandlers()?.Invoke(s, e);
        _dragOverWrapper = (s, e) => _getDragOverHandlers()?.Invoke(s, e);
        _dragLeftWrapper = (s, e) => _getDragLeftHandlers()?.Invoke(s, e);
        _dropCompletedWrapper = (s, e) => _getDropCompletedHandlers()?.Invoke(s, e);

        _navigateInPlaceAsync = navigationHooks.NavigateInPlaceAsync;
        _getInitialZoomFactor = navigationHooks.GetInitialZoomFactor;
        _applyInitialZoomFactor = navigationHooks.ApplyInitialZoomFactor;
    }

    public void Attach(IWebViewCoreControlEvents core)
    {
        ArgumentNullException.ThrowIfNull(core);

        if (ReferenceEquals(_attachedCore, core))
            return;

        Detach();

        _navigationStarted = (_, e) => _raiseNavigationStarted(e);
        _navigationCompleted = (_, e) => _raiseNavigationCompleted(e);
        _newWindowRequested = async (_, e) =>
        {
            _raiseNewWindowRequested(e);
            if (!e.Handled && e.Uri is not null)
                await _navigateInPlaceAsync(e.Uri).ConfigureAwait(false);
        };
        _webMessageReceived = (_, e) => _raiseWebMessageReceived(e);
        _webResourceRequested = (_, e) => _raiseWebResourceRequested(e);
        _environmentRequested = (_, e) => _raiseEnvironmentRequested(e);
        _downloadRequested = (_, e) => _raiseDownloadRequested(e);
        _permissionRequested = (_, e) => _raisePermissionRequested(e);
        _adapterCreated = (_, e) => _raiseAdapterCreated(e);
        _adapterDestroyed = (_, _) => _raiseAdapterDestroyed();
        _zoomFactorChanged = (_, zoom) => _raiseZoomFactorChanged(zoom);

        core.NavigationStarted += _navigationStarted;
        core.NavigationCompleted += _navigationCompleted;
        core.NewWindowRequested += _newWindowRequested;
        core.WebMessageReceived += _webMessageReceived;
        core.WebResourceRequested += _webResourceRequested;
        core.EnvironmentRequested += _environmentRequested;
        core.DownloadRequested += _downloadRequested;
        core.PermissionRequested += _permissionRequested;
        core.AdapterCreated += _adapterCreated;
        core.AdapterDestroyed += _adapterDestroyed;
        core.ZoomFactorChanged += _zoomFactorChanged;

        core.ContextMenuRequested += _contextMenuWrapper;
        core.DragEntered += _dragEnteredWrapper;
        core.DragOver += _dragOverWrapper;
        core.DragLeft += _dragLeftWrapper;
        core.DropCompleted += _dropCompletedWrapper;

        var zoom = _getInitialZoomFactor();
        if (Math.Abs(zoom - 1.0) > 0.001)
            _applyInitialZoomFactor(zoom);

        _attachedCore = core;
    }

    public void Detach()
    {
        var core = _attachedCore;
        if (core is null)
            return;

        if (_navigationStarted is not null) core.NavigationStarted -= _navigationStarted;
        if (_navigationCompleted is not null) core.NavigationCompleted -= _navigationCompleted;
        if (_newWindowRequested is not null) core.NewWindowRequested -= _newWindowRequested;
        if (_webMessageReceived is not null) core.WebMessageReceived -= _webMessageReceived;
        if (_webResourceRequested is not null) core.WebResourceRequested -= _webResourceRequested;
        if (_environmentRequested is not null) core.EnvironmentRequested -= _environmentRequested;
        if (_downloadRequested is not null) core.DownloadRequested -= _downloadRequested;
        if (_permissionRequested is not null) core.PermissionRequested -= _permissionRequested;
        if (_adapterCreated is not null) core.AdapterCreated -= _adapterCreated;
        if (_adapterDestroyed is not null) core.AdapterDestroyed -= _adapterDestroyed;
        if (_zoomFactorChanged is not null) core.ZoomFactorChanged -= _zoomFactorChanged;

        core.ContextMenuRequested -= _contextMenuWrapper;
        core.DragEntered -= _dragEnteredWrapper;
        core.DragOver -= _dragOverWrapper;
        core.DragLeft -= _dragLeftWrapper;
        core.DropCompleted -= _dropCompletedWrapper;

        _attachedCore = null;
    }
}
