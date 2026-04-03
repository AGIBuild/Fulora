using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreAdapterEventRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void Constructor_requires_host()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreAdapterEventRuntime(null!, _dispatcher, NullLogger.Instance));
    }

    [Fact]
    public void Constructor_requires_dispatcher()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreAdapterEventRuntime(new TestAdapterEventHost(), null!, NullLogger.Instance));
    }

    [Fact]
    public void Constructor_requires_logger()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreAdapterEventRuntime(new TestAdapterEventHost(), _dispatcher, null!));
    }

    [Fact]
    public void HandleNewWindowRequested_when_unhandled_navigates_in_place()
    {
        var host = new TestAdapterEventHost();
        var runtime = new WebViewCoreAdapterEventRuntime(host, _dispatcher, NullLogger.Instance);
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        runtime.HandleAdapterNewWindowRequested(args);

        Assert.Same(args, host.LastNewWindowArgs);
        Assert.Equal(args.Uri, host.LastNavigatedUri);
        Assert.Equal(1, host.NavigateCallCount);
    }

    [Fact]
    public void HandleNewWindowRequested_when_handled_skips_fallback_navigation()
    {
        var host = new TestAdapterEventHost
        {
            OnRaiseNewWindowRequested = args => args.Handled = true
        };
        var runtime = new WebViewCoreAdapterEventRuntime(host, _dispatcher, NullLogger.Instance);
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        runtime.HandleAdapterNewWindowRequested(args);

        Assert.Same(args, host.LastNewWindowArgs);
        Assert.Null(host.LastNavigatedUri);
        Assert.Equal(0, host.NavigateCallCount);
    }

    [Fact]
    public void HandleNewWindowRequested_queued_then_disposed_is_ignored()
    {
        var host = new TestAdapterEventHost();
        var runtime = new WebViewCoreAdapterEventRuntime(host, _dispatcher, NullLogger.Instance);
        var args = new NewWindowRequestedEventArgs(new Uri("https://example.test/popup"));

        RunOnBackgroundThread(() => runtime.HandleAdapterNewWindowRequested(args));
        host.IsDisposed = true;
        _dispatcher.RunAll();

        Assert.Null(host.LastNewWindowArgs);
        Assert.Null(host.LastNavigatedUri);
        Assert.Equal(0, host.NavigateCallCount);
    }

    [Fact]
    public void HandleWebResourceRequested_on_background_thread_dispatches_to_ui()
    {
        var host = new TestAdapterEventHost();
        var runtime = new WebViewCoreAdapterEventRuntime(host, _dispatcher, NullLogger.Instance);
        var args = new WebResourceRequestedEventArgs(new Uri("https://example.test/resource"), "GET");

        RunOnBackgroundThread(() => runtime.HandleAdapterWebResourceRequested(args));

        Assert.Null(host.LastWebResourceArgs);
        _dispatcher.RunAll();

        Assert.Same(args, host.LastWebResourceArgs);
        Assert.Equal(_dispatcher.UiThreadId, host.LastWebResourceThreadId);
    }

    [Fact]
    public void HandlePermissionRequested_when_adapter_destroyed_is_ignored()
    {
        var host = new TestAdapterEventHost
        {
            IsAdapterDestroyed = true
        };
        var runtime = new WebViewCoreAdapterEventRuntime(host, _dispatcher, NullLogger.Instance);
        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.test"));

        runtime.HandleAdapterPermissionRequested(args);

        Assert.Null(host.LastPermissionArgs);
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

    private sealed class TestAdapterEventHost : IWebViewCoreAdapterEventHost
    {
        public bool IsDisposed { get; set; }

        public bool IsAdapterDestroyed { get; set; }

        public Action<NewWindowRequestedEventArgs>? OnRaiseNewWindowRequested { get; set; }

        public Uri? LastNavigatedUri { get; private set; }

        public int NavigateCallCount { get; private set; }

        public NewWindowRequestedEventArgs? LastNewWindowArgs { get; private set; }

        public WebResourceRequestedEventArgs? LastWebResourceArgs { get; private set; }

        public int? LastWebResourceThreadId { get; private set; }

        public PermissionRequestedEventArgs? LastPermissionArgs { get; private set; }

        public Task NavigateAsync(Uri uri)
        {
            NavigateCallCount++;
            LastNavigatedUri = uri;
            return Task.CompletedTask;
        }

        public void RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
        {
            LastNewWindowArgs = args;
            OnRaiseNewWindowRequested?.Invoke(args);
        }

        public void RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
        {
            LastWebResourceArgs = args;
            LastWebResourceThreadId = Environment.CurrentManagedThreadId;
        }

        public void RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args)
        {
        }

        public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
        {
        }

        public void RaisePermissionRequested(PermissionRequestedEventArgs args)
            => LastPermissionArgs = args;
    }
}
