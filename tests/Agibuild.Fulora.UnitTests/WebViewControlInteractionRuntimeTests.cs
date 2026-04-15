using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlInteractionRuntimeTests
{
    [Fact]
    public void AddContextMenuRequestedHandler_stores_handler_before_core_attach()
    {
        var runtime = new WebViewControlInteractionRuntime();
        EventHandler<ContextMenuRequestedEventArgs> handler = (_, _) => { };

        runtime.AddContextMenuRequestedHandler(core: null, handler);

        Assert.NotNull(runtime.ContextMenuRequestedHandlers);
    }

    [Fact]
    public void AddDragEnteredHandler_subscribes_immediately_when_core_is_attached()
    {
        var runtime = new WebViewControlInteractionRuntime();
        var core = new StubCoreEvents();
        var callCount = 0;

        runtime.AddDragEnteredHandler(core, (_, _) => callCount++);
        core.RaiseDragEntered(new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.Copy
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void RemoveDropCompletedHandler_unsubscribes_from_attached_core()
    {
        var runtime = new WebViewControlInteractionRuntime();
        var core = new StubCoreEvents();
        var callCount = 0;
        EventHandler<DropEventArgs> handler = (_, _) => callCount++;

        runtime.AddDropCompletedHandler(core, handler);
        runtime.RemoveDropCompletedHandler(core, handler);
        core.RaiseDropCompleted(new DropEventArgs
        {
            Payload = new DragDropPayload()
        });

        Assert.Equal(0, callCount);
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
