using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreAdapterEventRuntimeTests
{
    /// <summary>
    /// Observer that subscribes to the hub's adapter-facing events so tests can assert on the
    /// forwarded event arguments and the thread they were raised from.
    /// </summary>
    private sealed class AdapterEventObserver
    {
        public AdapterEventObserver(WebViewCoreEventHub hub)
        {
            hub.NewWindowRequested += (_, args) =>
            {
                LastNewWindowArgs = args;
                OnRaiseNewWindowRequested?.Invoke(args);
            };
            hub.WebResourceRequested += (_, args) =>
            {
                LastWebResourceArgs = args;
                LastWebResourceThreadId = Environment.CurrentManagedThreadId;
            };
            hub.PermissionRequested += (_, args) => LastPermissionArgs = args;
            hub.EnvironmentRequested += (_, _) => { };
            hub.DownloadRequested += (_, _) => { };
        }

        public Action<NewWindowRequestedEventArgs>? OnRaiseNewWindowRequested { get; set; }
        public NewWindowRequestedEventArgs? LastNewWindowArgs { get; private set; }
        public WebResourceRequestedEventArgs? LastWebResourceArgs { get; private set; }
        public int? LastWebResourceThreadId { get; private set; }
        public PermissionRequestedEventArgs? LastPermissionArgs { get; private set; }
    }

    private static (WebViewCoreAdapterEventRuntime Runtime,
        AdapterEventObserver Observer,
        WebViewCoreContext Context,
        TestDispatcher Dispatcher,
        FakeNavigator Navigator)
        CreateRuntime(WebViewLifecycleStateMachine? lifecycle = null)
    {
        var adapter = MockWebViewAdapter.Create();
        var dispatcher = new TestDispatcher();
        var hub = new WebViewCoreEventHub(new object());
        var context = WebViewCoreTestContext.Create(
            adapter,
            dispatcher: dispatcher,
            lifecycle: lifecycle,
            events: hub);
        var observer = new AdapterEventObserver(hub);
        var navigator = new FakeNavigator();
        var runtime = new WebViewCoreAdapterEventRuntime(context, navigator.NavigateAsync);
        return (runtime, observer, context, dispatcher, navigator);
    }

    [Fact]
    public void Constructor_requires_context()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreAdapterEventRuntime(null!, _ => Task.CompletedTask));
    }

    [Fact]
    public void Constructor_requires_navigate_callback()
    {
        var adapter = MockWebViewAdapter.Create();
        var context = WebViewCoreTestContext.Create(adapter);
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreAdapterEventRuntime(context, null!));
    }

    [Fact]
    public void HandleNewWindowRequested_when_unhandled_navigates_in_place()
    {
        var (runtime, observer, _, _, navigator) = CreateRuntime();
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        runtime.HandleAdapterNewWindowRequested(args);

        Assert.Same(args, observer.LastNewWindowArgs);
        Assert.Equal(args.Uri, navigator.LastNavigatedUri);
        Assert.Equal(1, navigator.NavigateCallCount);
    }

    [Fact]
    public void HandleNewWindowRequested_when_handled_skips_fallback_navigation()
    {
        var (runtime, observer, _, _, navigator) = CreateRuntime();
        observer.OnRaiseNewWindowRequested = args => args.Handled = true;
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        runtime.HandleAdapterNewWindowRequested(args);

        Assert.Same(args, observer.LastNewWindowArgs);
        Assert.Null(navigator.LastNavigatedUri);
        Assert.Equal(0, navigator.NavigateCallCount);
    }

    [Fact]
    public void HandleNewWindowRequested_queued_then_disposed_is_ignored()
    {
        var lifecycle = WebViewCoreTestContext.CreateReadyLifecycle();
        var (runtime, observer, _, dispatcher, navigator) = CreateRuntime(lifecycle: lifecycle);
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        RunOnBackgroundThread(() => runtime.HandleAdapterNewWindowRequested(args));
        lifecycle.TryTransitionToDisposed();
        dispatcher.RunAll();

        Assert.Null(observer.LastNewWindowArgs);
        Assert.Null(navigator.LastNavigatedUri);
        Assert.Equal(0, navigator.NavigateCallCount);
    }

    [Fact]
    public void HandleWebResourceRequested_on_background_thread_dispatches_to_ui()
    {
        var (runtime, observer, _, dispatcher, _) = CreateRuntime();
        var args = new WebResourceRequestedEventArgs(new Uri("https://example.test/resource"), "GET");

        RunOnBackgroundThread(() => runtime.HandleAdapterWebResourceRequested(args));

        Assert.Null(observer.LastWebResourceArgs);
        dispatcher.RunAll();

        Assert.Same(args, observer.LastWebResourceArgs);
        Assert.Equal(dispatcher.UiThreadId, observer.LastWebResourceThreadId);
    }

    [Fact]
    public void HandlePermissionRequested_when_adapter_destroyed_is_ignored()
    {
        var lifecycle = WebViewCoreTestContext.CreateReadyLifecycle();
        lifecycle.MarkAdapterDestroyedOnce(() => { });
        var (runtime, observer, _, _, _) = CreateRuntime(lifecycle: lifecycle);
        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.test"));

        runtime.HandleAdapterPermissionRequested(args);

        Assert.Null(observer.LastPermissionArgs);
    }

    private static void RunOnBackgroundThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    private sealed class FakeNavigator
    {
        public Uri? LastNavigatedUri { get; private set; }
        public int NavigateCallCount { get; private set; }

        public Task NavigateAsync(Uri uri)
        {
            NavigateCallCount++;
            LastNavigatedUri = uri;
            return Task.CompletedTask;
        }
    }
}
