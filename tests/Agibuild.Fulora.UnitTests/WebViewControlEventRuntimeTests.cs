using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlEventRuntimeTests
{
    [Fact]
    public async Task Attach_forwards_new_window_and_falls_back_to_in_place_navigation()
    {
        var navigatedTo = default(Uri);
        var raised = default(NewWindowRequestedEventArgs);
        var runtime = CreateRuntime(
            raiseNewWindowRequested: args => raised = args,
            navigateInPlaceAsync: uri =>
            {
                navigatedTo = uri;
                return Task.CompletedTask;
            },
            getInitialZoomFactor: () => 1.0);

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
        var runtime = CreateRuntime(
            getContextMenuHandlers: () => (_, _) => contextMenuCalls++,
            navigateInPlaceAsync: _ => Task.CompletedTask,
            getInitialZoomFactor: () => 1.5,
            applyInitialZoomFactor: zoom => zoomApplied = zoom);

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
        var runtime = CreateRuntime(
            raiseWebMessageReceived: _ => messages++,
            getContextMenuHandlers: () => (_, _) => messages++,
            navigateInPlaceAsync: _ => Task.CompletedTask,
            getInitialZoomFactor: () => 1.0);

        var core = new StubCoreEvents();
        runtime.Attach(core);
        runtime.Detach();

        core.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());
        core.RaiseContextMenuRequested(new ContextMenuRequestedEventArgs());

        Assert.Equal(0, messages);
    }

    [Fact]
    public void Attach_to_new_core_detaches_previous_core_handlers()
    {
        var messages = 0;
        var runtime = CreateRuntime(
            raiseWebMessageReceived: _ => messages++,
            navigateInPlaceAsync: _ => Task.CompletedTask,
            getInitialZoomFactor: () => 1.0);

        var firstCore = new StubCoreEvents();
        var secondCore = new StubCoreEvents();

        runtime.Attach(firstCore);
        runtime.Attach(secondCore);

        firstCore.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());
        secondCore.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());

        Assert.Equal(1, messages);
    }

    [Fact]
    public void Attach_to_same_core_does_not_duplicate_forwarders()
    {
        var messages = 0;
        var runtime = CreateRuntime(
            raiseWebMessageReceived: _ => messages++,
            getInitialZoomFactor: () => 1.0);

        var core = new StubCoreEvents();
        runtime.Attach(core);
        runtime.Attach(core);

        core.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());

        Assert.Equal(1, messages);
    }

    [Fact]
    public void Attach_to_same_core_does_not_reapply_initial_zoom()
    {
        var zoomApplyCount = 0;
        var runtime = CreateRuntime(
            getInitialZoomFactor: () => 1.5,
            applyInitialZoomFactor: _ => zoomApplyCount++);

        var core = new StubCoreEvents();
        runtime.Attach(core);
        runtime.Attach(core);

        Assert.Equal(1, zoomApplyCount);
    }

    [Fact]
    public void Attach_to_same_core_does_not_duplicate_interaction_handlers()
    {
        var contextMenuCalls = 0;
        var runtime = CreateRuntime(
            getContextMenuHandlers: () => (_, _) => contextMenuCalls++,
            getInitialZoomFactor: () => 1.0);

        var core = new StubCoreEvents();
        runtime.Attach(core);
        runtime.Attach(core);

        core.RaiseContextMenuRequested(new ContextMenuRequestedEventArgs());

        Assert.Equal(1, contextMenuCalls);
    }

#pragma warning disable CS0067
    private static WebViewControlEventRuntime CreateRuntime(
        Action<NewWindowRequestedEventArgs>? raiseNewWindowRequested = null,
        Action<WebMessageReceivedEventArgs>? raiseWebMessageReceived = null,
        Func<EventHandler<ContextMenuRequestedEventArgs>?>? getContextMenuHandlers = null,
        Func<Uri, Task>? navigateInPlaceAsync = null,
        Func<double>? getInitialZoomFactor = null,
        Action<double>? applyInitialZoomFactor = null)
        => new(
            callbacks: new WebViewControlEventCallbacks(
                raiseNavigationStarted: _ => { },
                raiseNavigationCompleted: _ => { },
                raiseNewWindowRequested: raiseNewWindowRequested ?? (_ => { }),
                raiseWebMessageReceived: raiseWebMessageReceived ?? (_ => { }),
                raiseWebResourceRequested: _ => { },
                raiseEnvironmentRequested: _ => { },
                raiseDownloadRequested: _ => { },
                raisePermissionRequested: _ => { },
                raiseAdapterCreated: _ => { },
                raiseAdapterDestroyed: () => { },
                raiseZoomFactorChanged: _ => { }),
            interactionHandlers: new WebViewControlInteractionAccessors(
                getContextMenuHandlers: getContextMenuHandlers ?? (() => null),
                getDragEnteredHandlers: () => null,
                getDragOverHandlers: () => null,
                getDragLeftHandlers: () => null,
                getDropCompletedHandlers: () => null),
            navigationHooks: new WebViewControlNavigationHooks(
                navigateInPlaceAsync: navigateInPlaceAsync ?? (_ => Task.CompletedTask),
                getInitialZoomFactor: getInitialZoomFactor ?? (() => 1.0),
                applyInitialZoomFactor: applyInitialZoomFactor ?? (_ => { })));

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
