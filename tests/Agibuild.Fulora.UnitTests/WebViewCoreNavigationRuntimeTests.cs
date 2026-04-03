using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreNavigationRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void Constructor_requires_host()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreNavigationRuntime(null!, _dispatcher, NullLogger.Instance));
    }

    [Fact]
    public void Constructor_requires_dispatcher()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreNavigationRuntime(new TestNavigationHost(), null!, NullLogger.Instance));
    }

    [Fact]
    public void Constructor_requires_logger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreNavigationRuntime(new TestNavigationHost(), _dispatcher, null!));
    }

    [Fact]
    public async Task NativeNavigationStarting_non_main_frame_is_auto_allowed()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            Guid.NewGuid(),
            new Uri("https://example.test/sub-frame"),
            false));

        Assert.True(decision.IsAllowed);
        Assert.Equal(Guid.Empty, decision.NavigationId);
        Assert.Null(host.ActiveNavigation);
        Assert.Empty(host.StartedEvents);
    }

    [Fact]
    public async Task StartNavigationRequest_supersedes_active_navigation_and_invokes_adapter()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
        _ = host.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));
        Guid? invokedNavigationId = null;

        var operationTask = await runtime.StartNavigationRequestCoreAsync(
            new Uri("https://example.test/next"),
            navigationId =>
            {
                invokedNavigationId = navigationId;
                return Task.CompletedTask;
            },
            updateSource: true);

        Assert.Equal(NavigationCompletedStatus.Superseded, host.CompletedStatuses[0]);
        Assert.NotNull(invokedNavigationId);
        Assert.Equal(invokedNavigationId, host.ActiveNavigation!.Value.NavigationId);
        Assert.Equal(new Uri("https://example.test/next"), host.LastSource);
        Assert.Single(host.StartedEvents);
        Assert.False(operationTask.IsCompleted);
    }

    [Fact]
    public async Task StartNavigationRequest_canceled_by_handler_completes_as_canceled_without_invoking_adapter()
    {
        var host = new TestNavigationHost
        {
            CancelStartedNavigations = true
        };
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
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
        Assert.Equal([NavigationCompletedStatus.Canceled], host.CompletedStatuses);
        await operationTask;
    }

    [Fact]
    public async Task StartNavigationRequest_adapter_exception_completes_as_failure()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);

        var operationTask = await runtime.StartNavigationRequestCoreAsync(
            new Uri("https://example.test/fail"),
            _ => Task.FromException(new InvalidOperationException("adapter boom")),
            updateSource: true);

        Assert.Equal(NavigationCompletedStatus.Failure, host.LastCompletedStatus);
        Assert.Equal("adapter boom", host.LastCompletedError!.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationTask);
    }

    [Fact]
    public void StartCommandNavigation_canceled_by_handler_returns_empty_guid()
    {
        var host = new TestNavigationHost
        {
            CancelStartedNavigations = true
        };
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);

        var navigationId = runtime.StartCommandNavigation(new Uri("https://example.test/command"));

        Assert.Equal(Guid.Empty, navigationId);
        Assert.Equal([NavigationCompletedStatus.Canceled], host.CompletedStatuses);
    }

    [Fact]
    public async Task NativeNavigationStarting_redirect_reuses_active_navigation_id()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
        var correlationId = Guid.NewGuid();
        var activeNavigationId = Guid.NewGuid();
        _ = host.SetActiveNavigation(activeNavigationId, correlationId, new Uri("https://example.test/start"));

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            correlationId,
            new Uri("https://example.test/redirected"),
            true));

        Assert.True(decision.IsAllowed);
        Assert.Equal(activeNavigationId, decision.NavigationId);
        Assert.Equal(new Uri("https://example.test/redirected"), host.ActiveNavigation!.Value.RequestUri);
        Assert.Equal(new Uri("https://example.test/redirected"), host.LastSource);
        Assert.Single(host.StartedEvents);
        Assert.Equal(activeNavigationId, host.StartedEvents[0].NavigationId);
    }

    [Fact]
    public async Task NativeNavigationStarting_when_redirect_is_canceled_denies_and_completes_navigation()
    {
        var host = new TestNavigationHost
        {
            CancelStartedNavigations = true
        };
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
        var correlationId = Guid.NewGuid();
        var activeNavigationId = Guid.NewGuid();
        _ = host.SetActiveNavigation(activeNavigationId, correlationId, new Uri("https://example.test/start"));

        var decision = await runtime.OnNativeNavigationStartingAsync(new NativeNavigationStartingInfo(
            correlationId,
            new Uri("https://example.test/redirected"),
            true));

        Assert.False(decision.IsAllowed);
        Assert.Equal(activeNavigationId, decision.NavigationId);
        Assert.Equal(NavigationCompletedStatus.Canceled, host.LastCompletedStatus);
        Assert.Null(host.ActiveNavigation);
    }

    [Fact]
    public void AdapterNavigationCompleted_id_mismatch_is_ignored()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
        _ = host.SetActiveNavigation(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://example.test/active"));

        runtime.HandleAdapterNavigationCompleted(new NavigationCompletedEventArgs(
            Guid.NewGuid(),
            new Uri("https://example.test/other"),
            NavigationCompletedStatus.Success,
            error: null));

        Assert.Null(host.LastCompletedStatus);
        Assert.NotNull(host.ActiveNavigation);
    }

    [Fact]
    public void AdapterNavigationCompleted_success_updates_request_uri_and_completes_navigation()
    {
        var host = new TestNavigationHost();
        var runtime = new WebViewCoreNavigationRuntime(host, _dispatcher, NullLogger.Instance);
        var navigationId = Guid.NewGuid();
        _ = host.SetActiveNavigation(navigationId, navigationId, new Uri("https://example.test/active"));

        runtime.HandleAdapterNavigationCompleted(new NavigationCompletedEventArgs(
            navigationId,
            new Uri("https://example.test/success"),
            NavigationCompletedStatus.Success,
            error: null));

        Assert.Equal(NavigationCompletedStatus.Success, host.LastCompletedStatus);
        Assert.Null(host.LastCompletedError);
        Assert.Equal(new Uri("https://example.test/success"), host.LastUpdatedRequestUri);
    }

    private sealed class TestNavigationHost : IWebViewCoreNavigationHost
    {
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;

        public bool IsDisposed { get; set; }

        public bool IsAdapterDestroyed { get; set; }

        public bool CancelStartedNavigations { get; set; }

        public WebViewCoreNavigationState? ActiveNavigation { get; private set; }

        public Uri? LastSource { get; private set; }

        public Uri? LastUpdatedRequestUri { get; private set; }

        public NavigationCompletedStatus? LastCompletedStatus { get; private set; }

        public Exception? LastCompletedError { get; private set; }

        public List<NavigationStartingEventArgs> StartedEvents { get; } = [];

        public List<NavigationCompletedStatus> CompletedStatuses { get; } = [];

        public void CompleteActiveNavigation(NavigationCompletedStatus status, Exception? error)
        {
            CompletedStatuses.Add(status);
            LastCompletedStatus = status;
            LastCompletedError = error;
            if (status == NavigationCompletedStatus.Failure)
            {
                ActiveNavigationTaskSource?.TrySetException(error ?? new InvalidOperationException("Navigation failed."));
            }
            else
            {
                ActiveNavigationTaskSource?.TrySetResult();
            }

            ActiveNavigation = null;
            ActiveNavigationTaskSource = null;
        }

        public void RaiseNavigationStarting(NavigationStartingEventArgs args)
        {
            StartedEvents.Add(args);
            if (CancelStartedNavigations)
            {
                args.Cancel = true;
            }
        }

        public TaskCompletionSource? ActiveNavigationTaskSource { get; private set; }

        public Task SetActiveNavigation(Guid navigationId, Guid correlationId, Uri requestUri)
        {
            ActiveNavigation = new WebViewCoreNavigationState(navigationId, correlationId, requestUri);
            ActiveNavigationTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return ActiveNavigationTaskSource.Task;
        }

        public void SetSource(Uri uri)
            => LastSource = uri;

        public void ThrowIfNotOnUiThread(string apiName)
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
            {
                throw new InvalidOperationException($"'{apiName}' must be called on the UI thread.");
            }
        }

        public bool TryGetActiveNavigation(out WebViewCoreNavigationState state)
        {
            if (ActiveNavigation is { } active)
            {
                state = active;
                return true;
            }

            state = default;
            return false;
        }

        public void UpdateActiveNavigationRequestUri(Uri requestUri)
        {
            LastUpdatedRequestUri = requestUri;
            if (ActiveNavigation is { } active)
            {
                ActiveNavigation = active with { RequestUri = requestUri };
            }
        }
    }
}
