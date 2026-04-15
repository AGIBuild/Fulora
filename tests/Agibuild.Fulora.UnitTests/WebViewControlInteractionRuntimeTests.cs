using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlInteractionRuntimeTests
{
    [Fact]
    public void AddContextMenuRequestedHandler_stores_handler_in_aggregate()
    {
        var runtime = new WebViewControlInteractionRuntime();
        EventHandler<ContextMenuRequestedEventArgs> handler = (_, _) => { };

        runtime.AddContextMenuRequestedHandler(handler);

        Assert.NotNull(runtime.ContextMenuRequestedHandlers);
    }

    [Fact]
    public void AddDragEnteredHandler_stores_handler_in_aggregate()
    {
        var runtime = new WebViewControlInteractionRuntime();
        var callCount = 0;

        runtime.AddDragEnteredHandler((_, _) => callCount++);
        runtime.DragEnteredHandlers?.Invoke(null!, new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.Copy
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void RemoveDropCompletedHandler_removes_handler_from_aggregate()
    {
        var runtime = new WebViewControlInteractionRuntime();
        EventHandler<DropEventArgs> handler = (_, _) => { };

        runtime.AddDropCompletedHandler(handler);
        runtime.RemoveDropCompletedHandler(handler);

        Assert.Null(runtime.DropCompletedHandlers);
    }

#pragma warning disable CS0067
    private sealed class StubCoreEvents : IWebViewCoreControlEvents
    {
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

        public void RaiseDragEntered(DragEventArgs args) => DragEntered?.Invoke(this, args);

        public void RaiseDropCompleted(DropEventArgs args) => DropCompleted?.Invoke(this, args);
    }
#pragma warning restore CS0067
}
