using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlEventRuntimeTests
{
    [Fact]
    public async Task Attach_forwards_new_window_and_falls_back_to_in_place_navigation()
    {
        var navigatedTo = default(Uri);
        var raised = default(NewWindowRequestedEventArgs);
        var runtime = new WebViewControlEventRuntime(
            _ => { },
            _ => { },
            args => raised = args,
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            () => { },
            _ => { },
            () => null,
            () => null,
            () => null,
            () => null,
            () => null,
            uri =>
            {
                navigatedTo = uri;
                return Task.CompletedTask;
            },
            () => 1.0,
            _ => { });

        var core = new StubCoreEvents();
        runtime.Attach(core);

        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));
        core.RaiseNewWindowRequested(args);
        await Task.Yield();

        Assert.Same(args, raised);
        Assert.Equal(args.Uri, navigatedTo);
    }

    [Fact]
    public void Attach_applies_existing_control_handlers_and_initial_zoom()
    {
        var zoomApplied = 1.0;
        var contextMenuCalls = 0;
        var runtime = new WebViewControlEventRuntime(
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            () => { },
            _ => { },
            () => (_, _) => contextMenuCalls++,
            () => null,
            () => null,
            () => null,
            () => null,
            _ => Task.CompletedTask,
            () => 1.5,
            zoom => zoomApplied = zoom);

        var core = new StubCoreEvents();
        runtime.Attach(core);

        core.RaiseContextMenuRequested(new ContextMenuRequestedEventArgs());

        Assert.Equal(1, contextMenuCalls);
        Assert.Equal(1.5, zoomApplied);
    }

    [Fact]
    public void Detach_unhooks_forwarders_and_control_handlers()
    {
        var messages = 0;
        var runtime = new WebViewControlEventRuntime(
            _ => { },
            _ => { },
            _ => { },
            _ => messages++,
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            () => { },
            _ => { },
            () => (_, _) => messages++,
            () => null,
            () => null,
            () => null,
            () => null,
            _ => Task.CompletedTask,
            () => 1.0,
            _ => { });

        var core = new StubCoreEvents();
        runtime.Attach(core);
        runtime.Detach(core);

        core.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());
        core.RaiseContextMenuRequested(new ContextMenuRequestedEventArgs());

        Assert.Equal(0, messages);
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

        public void RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
            => NewWindowRequested?.Invoke(this, args);

        public void RaiseContextMenuRequested(ContextMenuRequestedEventArgs args)
            => ContextMenuRequested?.Invoke(this, args);

        public void RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
            => WebMessageReceived?.Invoke(this, args);
    }
#pragma warning restore CS0067
}
