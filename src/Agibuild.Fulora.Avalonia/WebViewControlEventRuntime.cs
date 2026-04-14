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

    public WebViewControlEventRuntime(
        Action<NavigationStartingEventArgs> raiseNavigationStarted,
        Action<NavigationCompletedEventArgs> raiseNavigationCompleted,
        Action<NewWindowRequestedEventArgs> raiseNewWindowRequested,
        Action<WebMessageReceivedEventArgs> raiseWebMessageReceived,
        Action<WebResourceRequestedEventArgs> raiseWebResourceRequested,
        Action<EnvironmentRequestedEventArgs> raiseEnvironmentRequested,
        Action<DownloadRequestedEventArgs> raiseDownloadRequested,
        Action<PermissionRequestedEventArgs> raisePermissionRequested,
        Action<AdapterCreatedEventArgs> raiseAdapterCreated,
        Action raiseAdapterDestroyed,
        Action<double> raiseZoomFactorChanged,
        Func<EventHandler<ContextMenuRequestedEventArgs>?> getContextMenuHandlers,
        Func<EventHandler<DragEventArgs>?> getDragEnteredHandlers,
        Func<EventHandler<DragEventArgs>?> getDragOverHandlers,
        Func<EventHandler<EventArgs>?> getDragLeftHandlers,
        Func<EventHandler<DropEventArgs>?> getDropCompletedHandlers,
        Func<Uri, Task> navigateInPlaceAsync,
        Func<double> getInitialZoomFactor,
        Action<double> applyInitialZoomFactor)
    {
        _raiseNavigationStarted = raiseNavigationStarted;
        _raiseNavigationCompleted = raiseNavigationCompleted;
        _raiseNewWindowRequested = raiseNewWindowRequested;
        _raiseWebMessageReceived = raiseWebMessageReceived;
        _raiseWebResourceRequested = raiseWebResourceRequested;
        _raiseEnvironmentRequested = raiseEnvironmentRequested;
        _raiseDownloadRequested = raiseDownloadRequested;
        _raisePermissionRequested = raisePermissionRequested;
        _raiseAdapterCreated = raiseAdapterCreated;
        _raiseAdapterDestroyed = raiseAdapterDestroyed;
        _raiseZoomFactorChanged = raiseZoomFactorChanged;
        _getContextMenuHandlers = getContextMenuHandlers;
        _getDragEnteredHandlers = getDragEnteredHandlers;
        _getDragOverHandlers = getDragOverHandlers;
        _getDragLeftHandlers = getDragLeftHandlers;
        _getDropCompletedHandlers = getDropCompletedHandlers;
        _navigateInPlaceAsync = navigateInPlaceAsync;
        _getInitialZoomFactor = getInitialZoomFactor;
        _applyInitialZoomFactor = applyInitialZoomFactor;
    }

    public void Attach(IWebViewCoreControlEvents core)
    {
        ArgumentNullException.ThrowIfNull(core);

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

        if (_getContextMenuHandlers() is { } context)
            core.ContextMenuRequested += context;
        if (_getDragEnteredHandlers() is { } dragEntered)
            core.DragEntered += dragEntered;
        if (_getDragOverHandlers() is { } dragOver)
            core.DragOver += dragOver;
        if (_getDragLeftHandlers() is { } dragLeft)
            core.DragLeft += dragLeft;
        if (_getDropCompletedHandlers() is { } drop)
            core.DropCompleted += drop;

        var zoom = _getInitialZoomFactor();
        if (Math.Abs(zoom - 1.0) > 0.001)
            _applyInitialZoomFactor(zoom);
    }

    public void Detach(IWebViewCoreControlEvents? core)
    {
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

        if (_getContextMenuHandlers() is { } context)
            core.ContextMenuRequested -= context;
        if (_getDragEnteredHandlers() is { } dragEntered)
            core.DragEntered -= dragEntered;
        if (_getDragOverHandlers() is { } dragOver)
            core.DragOver -= dragOver;
        if (_getDragLeftHandlers() is { } dragLeft)
            core.DragLeft -= dragLeft;
        if (_getDropCompletedHandlers() is { } drop)
            core.DropCompleted -= drop;
    }
}
