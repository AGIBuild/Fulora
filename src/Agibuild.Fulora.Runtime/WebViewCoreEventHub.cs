namespace Agibuild.Fulora;

/// <summary>
/// Single source of truth for every WebView-observable event. Implements
/// <see cref="IWebViewCoreControlEvents"/> so host surfaces (the public <see cref="WebViewCore"/>
/// type and the Avalonia control) can delegate subscription management here without owning any
/// <c>EventHandler&lt;T&gt;</c> fields of their own.
/// </summary>
/// <remarks>
/// <para>
/// All <c>RaiseXxx</c> methods use the <c>sender</c> captured at construction time, which is the
/// owning <see cref="WebViewCore"/> instance. External subscribers continue to observe
/// <see cref="WebViewCore"/> as the event sender, preserving the contract that existed before the
/// hub was extracted.
/// </para>
/// <para>
/// Subscription order remains observable: handlers run in the order they were added. Runtimes that
/// subscribe during construction therefore run <em>before</em> handlers added by external callers
/// after <see cref="WebViewCore"/> is constructed. This ordering is intentional and relied upon by
/// <see cref="WebViewCoreNavigationRuntime"/> when it raises <c>NavigationCompleted</c> followed by
/// the bridge stub re-injection hook.
/// </para>
/// </remarks>
internal sealed class WebViewCoreEventHub : IWebViewCoreControlEvents
{
    private readonly object _sender;

    public WebViewCoreEventHub(object sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public event EventHandler<NavigationStartingEventArgs>? NavigationStarted;
    public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated;
    public event EventHandler? AdapterDestroyed;
    public event EventHandler<double>? ZoomFactorChanged;
    public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
    public event EventHandler<DragEventArgs>? DragEntered;
    public event EventHandler<DragEventArgs>? DragOver;
    public event EventHandler<EventArgs>? DragLeft;
    public event EventHandler<DropEventArgs>? DropCompleted;

    public void RaiseNavigationStarted(NavigationStartingEventArgs args)
        => NavigationStarted?.Invoke(_sender, args);

    public void RaiseNavigationCompleted(NavigationCompletedEventArgs args)
        => NavigationCompleted?.Invoke(_sender, args);

    public void RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
        => NewWindowRequested?.Invoke(_sender, args);

    public void RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
        => WebMessageReceived?.Invoke(_sender, args);

    public void RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
        => WebResourceRequested?.Invoke(_sender, args);

    public void RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args)
        => EnvironmentRequested?.Invoke(_sender, args);

    public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
        => DownloadRequested?.Invoke(_sender, args);

    public void RaisePermissionRequested(PermissionRequestedEventArgs args)
        => PermissionRequested?.Invoke(_sender, args);

    public void RaiseAdapterCreated(AdapterCreatedEventArgs args)
        => AdapterCreated?.Invoke(_sender, args);

    public void RaiseAdapterDestroyed()
        => AdapterDestroyed?.Invoke(_sender, EventArgs.Empty);

    public void RaiseZoomFactorChanged(double zoomFactor)
        => ZoomFactorChanged?.Invoke(_sender, zoomFactor);

    public void RaiseContextMenuRequested(ContextMenuRequestedEventArgs args)
        => ContextMenuRequested?.Invoke(_sender, args);

    public void RaiseDragEntered(DragEventArgs args)
        => DragEntered?.Invoke(_sender, args);

    public void RaiseDragOver(DragEventArgs args)
        => DragOver?.Invoke(_sender, args);

    public void RaiseDragLeft()
        => DragLeft?.Invoke(_sender, EventArgs.Empty);

    public void RaiseDropCompleted(DropEventArgs args)
        => DropCompleted?.Invoke(_sender, args);
}
