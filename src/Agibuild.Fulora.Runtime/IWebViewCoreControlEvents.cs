namespace Agibuild.Fulora;

internal interface IWebViewCoreControlEvents
{
    event EventHandler<NavigationStartingEventArgs>? NavigationStarted;
    event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
    event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
    event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
    event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
    event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
    event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
    event EventHandler<AdapterCreatedEventArgs>? AdapterCreated;
    event EventHandler? AdapterDestroyed;
    event EventHandler<double>? ZoomFactorChanged;
    event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
    event EventHandler<DragEventArgs>? DragEntered;
    event EventHandler<DragEventArgs>? DragOver;
    event EventHandler<EventArgs>? DragLeft;
    event EventHandler<DropEventArgs>? DropCompleted;
}
