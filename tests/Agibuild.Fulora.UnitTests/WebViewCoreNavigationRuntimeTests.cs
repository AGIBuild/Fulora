using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreNavigationRuntimeTests
{
    /// <summary>
    /// Gathers events raised on <see cref="WebViewCoreEventHub"/> and tracks the bridge-reinject
    /// callback count. Replaces the former per-test host mock — since the runtime now funnels events
    /// through the shared hub, the observer simply subscribes to the hub's event surface.
    /// </summary>
    private sealed class NavigationObserver
    {
        public NavigationObserver(WebViewCoreEventHub hub)
        {
            hub.NavigationStarted += (_, args) =>
            {
                StartedEvents.Add(args);
                if (CancelStartedNavigations)
                {
                    args.Cancel = true;
                }
            };
            hub.NavigationCompleted += (_, args) => CompletedEvents.Add(args);
        }

        public bool CancelStartedNavigations { get; set; }

        public List<NavigationStartingEventArgs> StartedEvents { get; } = [];

        public List<NavigationCompletedEventArgs> CompletedEvents { get; } = [];

        public int BridgeReinjectCalls { get; private set; }

        public Action BridgeReinjectCallback => () => BridgeReinjectCalls++;
    }

    private static (WebViewCoreNavigationRuntime Runtime, NavigationObserver Observer, WebViewCoreContext Context)
        CreateRuntime(WebViewLifecycleStateMachine? lifecycle = null)
    {
        var adapter = MockWebViewAdapter.Create();
        var hub = new WebViewCoreEventHub(new object());
        var context = WebViewCoreTestContext.Create(adapter, lifecycle: lifecycle, events: hub);
        var observer = new NavigationObserver(hub);
        var runtime = new WebViewCoreNavigationRuntime(context, observer.BridgeReinjectCallback);
        return (runtime, observer, context);
    }

    [Fact]
    public void Constructor_requires_context()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreNavigationRuntime(null!, () => { }));
    }

    [Fact]
    public void Constructor_requires_reinject_callback()
    {
        var adapter = MockWebViewAdapter.Create();
        var context = WebViewCoreTestContext.Create(adapter);
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreNavigationRuntime(context, null!));
    }

    [Fact]
    public async Task NativeNavigationStarting_non_main_frame_is_auto_allowed()
    {
        var (runtime, observer, _) = CreateRuntime();

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            Guid.NewGuid(),
            new Uri("https://example.test/sub-frame"),
            false));

        Assert.True(decision.IsAllowed);
        Assert.Equal(Guid.Empty, decision.NavigationId);
        Assert.False(runtime.IsLoading);
        Assert.Empty(observer.StartedEvents);
    }

    [Fact]
    public async Task StartNavigationRequest_supersedes_active_navigation_and_invokes_adapter()
    {
        var (runtime, observer, _) = CreateRuntime();

        // Seed an "active" navigation via the public API so the runtime owns its own state.
        var preExistingTask = runtime.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));
        Guid? invokedNavigationId = null;

        var operationTask = await runtime.StartNavigationRequestCoreAsync(
            new Uri("https://example.test/next"),
            navigationId =>
            {
                invokedNavigationId = navigationId;
                return Task.CompletedTask;
            },
            updateSource: true);

        Assert.True(preExistingTask.IsCompletedSuccessfully);
        Assert.Equal([NavigationCompletedStatus.Superseded], observer.CompletedEvents.Select(e => e.Status).ToArray());

        Assert.NotNull(invokedNavigationId);
        Assert.True(runtime.TryGetActiveNavigation(out var active));
        Assert.Equal(invokedNavigationId, active.NavigationId);

        Assert.Equal(new Uri("https://example.test/next"), runtime.CurrentSource);
        Assert.Single(observer.StartedEvents);
        Assert.False(operationTask.IsCompleted);
    }

    [Fact]
    public async Task StartNavigationRequest_canceled_by_handler_completes_as_canceled_without_invoking_adapter()
    {
        var (runtime, observer, _) = CreateRuntime();
        observer.CancelStartedNavigations = true;
        var adapterInvoked = false;

        var operationTask = await runtime.StartNavigationRequestCoreAsync(
            new Uri("https://example.test/cancel"),
            _ =>
            {
                adapterInvoked = true;
                return Task.CompletedTask;
            },
            updateSource: true);

        Assert.False(adapterInvoked);
        Assert.Equal([NavigationCompletedStatus.Canceled], observer.CompletedEvents.Select(e => e.Status).ToArray());
        await operationTask;
        Assert.False(runtime.IsLoading);
    }

    [Fact]
    public async Task StartNavigationRequest_adapter_exception_completes_as_failure()
    {
        var (runtime, observer, _) = CreateRuntime();

        var operationTask = await runtime.StartNavigationRequestCoreAsync(
            new Uri("https://example.test/fail"),
            _ => Task.FromException(new InvalidOperationException("adapter boom")),
            updateSource: true);

        var completed = Assert.Single(observer.CompletedEvents);
        Assert.Equal(NavigationCompletedStatus.Failure, completed.Status);
        var wrapped = await Assert.ThrowsAsync<WebViewNavigationException>(() => operationTask);
        Assert.IsType<InvalidOperationException>(wrapped.InnerException);
        Assert.Equal("adapter boom", wrapped.InnerException!.Message);
    }

    [Fact]
    public void StartCommandNavigation_canceled_by_handler_returns_empty_guid()
    {
        var (runtime, observer, _) = CreateRuntime();
        observer.CancelStartedNavigations = true;

        var navigationId = runtime.StartCommandNavigation(new Uri("https://example.test/command"));

        Assert.Equal(Guid.Empty, navigationId);
        Assert.Equal([NavigationCompletedStatus.Canceled], observer.CompletedEvents.Select(e => e.Status).ToArray());
        Assert.False(runtime.IsLoading);
    }

    [Fact]
    public async Task NativeNavigationStarting_redirect_reuses_active_navigation_id()
    {
        var (runtime, observer, _) = CreateRuntime();
        var correlationId = Guid.NewGuid();
        var activeNavigationId = Guid.NewGuid();
        _ = runtime.SetActiveNavigation(activeNavigationId, correlationId, new Uri("https://example.test/start"));

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            correlationId,
            new Uri("https://example.test/redirected"),
            true));

        Assert.True(decision.IsAllowed);
        Assert.Equal(activeNavigationId, decision.NavigationId);

        Assert.True(runtime.TryGetActiveNavigation(out var active));
        Assert.Equal(new Uri("https://example.test/redirected"), active.RequestUri);
        Assert.Equal(new Uri("https://example.test/redirected"), runtime.CurrentSource);

        Assert.Single(observer.StartedEvents);
        Assert.Equal(activeNavigationId, observer.StartedEvents[0].NavigationId);
    }

    [Fact]
    public async Task NativeNavigationStarting_when_redirect_is_canceled_denies_and_completes_navigation()
    {
        var (runtime, observer, _) = CreateRuntime();
        observer.CancelStartedNavigations = true;
        var correlationId = Guid.NewGuid();
        var activeNavigationId = Guid.NewGuid();
        _ = runtime.SetActiveNavigation(activeNavigationId, correlationId, new Uri("https://example.test/start"));

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            correlationId,
            new Uri("https://example.test/redirected"),
            true));

        Assert.False(decision.IsAllowed);
        Assert.Equal(activeNavigationId, decision.NavigationId);
        var completed = Assert.Single(observer.CompletedEvents);
        Assert.Equal(NavigationCompletedStatus.Canceled, completed.Status);
        Assert.False(runtime.IsLoading);
    }

    [Fact]
    public void AdapterNavigationCompleted_id_mismatch_is_ignored()
    {
        var (runtime, observer, _) = CreateRuntime();
        _ = runtime.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));

        runtime.HandleAdapterNavigationCompleted(new NavigationCompletedEventArgs(
            Guid.NewGuid(),
            new Uri("https://example.test/other"),
            NavigationCompletedStatus.Success,
            error: null));

        Assert.Empty(observer.CompletedEvents);
        Assert.True(runtime.IsLoading);
    }

    [Fact]
    public void AdapterNavigationCompleted_success_updates_request_uri_and_completes_navigation()
    {
        var (runtime, observer, _) = CreateRuntime();
        var navigationId = Guid.NewGuid();
        _ = runtime.SetActiveNavigation(navigationId, navigationId, new Uri("https://example.test/active"));

        runtime.HandleAdapterNavigationCompleted(new NavigationCompletedEventArgs(
            navigationId,
            new Uri("https://example.test/success"),
            NavigationCompletedStatus.Success,
            error: null));

        var completed = Assert.Single(observer.CompletedEvents);
        Assert.Equal(NavigationCompletedStatus.Success, completed.Status);
        Assert.Null(completed.Error);
        Assert.Equal(new Uri("https://example.test/success"), completed.RequestUri);
        Assert.Equal(1, observer.BridgeReinjectCalls);
        Assert.False(runtime.IsLoading);
    }

    [Fact]
    public void FaultActiveForDispose_is_silent_and_clears_active_navigation()
    {
        var (runtime, observer, _) = CreateRuntime();
        var operationTask = runtime.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));

        runtime.FaultActiveForDispose(new ObjectDisposedException("WebViewCore"));

        Assert.Empty(observer.CompletedEvents);
        Assert.False(runtime.IsLoading);
        Assert.True(operationTask.IsFaulted);
        Assert.IsType<ObjectDisposedException>(operationTask.Exception!.InnerException);
    }

    [Fact]
    public void FaultActiveForDispose_when_no_active_navigation_is_noop()
    {
        var (runtime, observer, _) = CreateRuntime();

        runtime.FaultActiveForDispose(new ObjectDisposedException("WebViewCore"));

        Assert.Empty(observer.CompletedEvents);
        Assert.False(runtime.IsLoading);
    }

    [Fact]
    public void TryStopActiveNavigation_when_no_active_returns_false()
    {
        var (runtime, observer, _) = CreateRuntime();

        Assert.False(runtime.TryStopActiveNavigation());
        Assert.Empty(observer.CompletedEvents);
    }

    [Fact]
    public void TryStopActiveNavigation_cancels_active_navigation()
    {
        var (runtime, observer, _) = CreateRuntime();
        var operationTask = runtime.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));

        Assert.True(runtime.TryStopActiveNavigation());
        var completed = Assert.Single(observer.CompletedEvents);
        Assert.Equal(NavigationCompletedStatus.Canceled, completed.Status);
        Assert.True(operationTask.IsCompletedSuccessfully);
        Assert.False(runtime.IsLoading);
    }
}
