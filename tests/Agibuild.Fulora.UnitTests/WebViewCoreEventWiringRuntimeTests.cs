using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreEventWiringRuntimeTests
{
    private static ILogger Logger()
        => NullLoggerFactory.Instance.CreateLogger("test");

    private static WebViewAdapterEventRouter Router(
        Action<NavigationCompletedEventArgs>? onNavigationCompleted = null,
        Action<NewWindowRequestedEventArgs>? onNewWindowRequested = null,
        Action<WebMessageReceivedEventArgs>? onWebMessageReceived = null,
        Action<WebResourceRequestedEventArgs>? onWebResourceRequested = null,
        Action<EnvironmentRequestedEventArgs>? onEnvironmentRequested = null,
        Action<DownloadRequestedEventArgs>? onDownloadRequested = null,
        Action<PermissionRequestedEventArgs>? onPermissionRequested = null)
        => new(
            OnNavigationCompleted: onNavigationCompleted ?? (_ => { }),
            OnNewWindowRequested: onNewWindowRequested ?? (_ => { }),
            OnWebMessageReceived: onWebMessageReceived ?? (_ => { }),
            OnWebResourceRequested: onWebResourceRequested ?? (_ => { }),
            OnEnvironmentRequested: onEnvironmentRequested ?? (_ => { }),
            OnDownloadRequested: onDownloadRequested ?? (_ => { }),
            OnPermissionRequested: onPermissionRequested ?? (_ => { }));

    [Fact]
    public void Constructor_subscribes_all_adapter_events()
    {
        var navigation = 0;
        var newWindow = 0;
        var webMessage = 0;
        var webResource = 0;
        var environment = 0;
        var download = 0;
        var permission = 0;

        var adapter = new FullAdapterStub();
        using var _ = new WebViewCoreEventWiringRuntime(
            adapter,
            Logger(),
            Router(
                onNavigationCompleted: _ => navigation++,
                onNewWindowRequested: _ => newWindow++,
                onWebMessageReceived: _ => webMessage++,
                onWebResourceRequested: _ => webResource++,
                onEnvironmentRequested: _ => environment++,
                onDownloadRequested: _ => download++,
                onPermissionRequested: _ => permission++));

        adapter.RaiseNavigationCompleted(new NavigationCompletedEventArgs());
        adapter.RaiseNewWindowRequested(new NewWindowRequestedEventArgs(new Uri("https://x.test/popup")));
        adapter.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());
        adapter.RaiseWebResourceRequested(new WebResourceRequestedEventArgs());
        adapter.RaiseEnvironmentRequested(new EnvironmentRequestedEventArgs());
        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(new Uri("https://x.test/file")));
        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Geolocation));

        Assert.Equal(1, navigation);
        Assert.Equal(1, newWindow);
        Assert.Equal(1, webMessage);
        Assert.Equal(1, webResource);
        Assert.Equal(1, environment);
        Assert.Equal(1, download);
        Assert.Equal(1, permission);
    }

    [Fact]
    public void Dispose_unhooks_all_adapter_events()
    {
        // Guards the symmetry contract of `WebViewCoreEventWiringRuntime.Dispose()` for every
        // adapter-plane event wired up in the constructor. If any `-=` line is removed or
        // misrouted, the corresponding counter below stops being zero and this test fails with
        // a precise diagnostic on exactly which subscription leaked.
        var navigation = 0;
        var newWindow = 0;
        var webMessage = 0;
        var webResource = 0;
        var environment = 0;
        var download = 0;
        var permission = 0;

        var adapter = new FullAdapterStub();
        var runtime = new WebViewCoreEventWiringRuntime(
            adapter,
            Logger(),
            Router(
                onNavigationCompleted: _ => navigation++,
                onNewWindowRequested: _ => newWindow++,
                onWebMessageReceived: _ => webMessage++,
                onWebResourceRequested: _ => webResource++,
                onEnvironmentRequested: _ => environment++,
                onDownloadRequested: _ => download++,
                onPermissionRequested: _ => permission++));

        runtime.Dispose();

        adapter.RaiseNavigationCompleted(new NavigationCompletedEventArgs());
        adapter.RaiseNewWindowRequested(new NewWindowRequestedEventArgs(new Uri("https://x.test/popup")));
        adapter.RaiseWebMessageReceived(new WebMessageReceivedEventArgs());
        adapter.RaiseWebResourceRequested(new WebResourceRequestedEventArgs());
        adapter.RaiseEnvironmentRequested(new EnvironmentRequestedEventArgs());
        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(new Uri("https://x.test/file")));
        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(WebViewPermissionKind.Geolocation));

        Assert.Equal(0, navigation);
        Assert.Equal(0, newWindow);
        Assert.Equal(0, webMessage);
        Assert.Equal(0, webResource);
        Assert.Equal(0, environment);
        Assert.Equal(0, download);
        Assert.Equal(0, permission);
    }

    [Fact]
    public void Constructor_rejects_null_router_actions()
    {
        var adapter = new FullAdapterStub();

        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreEventWiringRuntime(
                adapter,
                Logger(),
                new WebViewAdapterEventRouter(
                    OnNavigationCompleted: null!,
                    OnNewWindowRequested: _ => { },
                    OnWebMessageReceived: _ => { },
                    OnWebResourceRequested: _ => { },
                    OnEnvironmentRequested: _ => { },
                    OnDownloadRequested: _ => { },
                    OnPermissionRequested: _ => { })));
    }

    [Fact]
    public void Constructor_rejects_null_adapter()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreEventWiringRuntime(null!, Logger(), Router()));
    }

    [Fact]
    public void Constructor_rejects_null_logger()
    {
        var adapter = new FullAdapterStub();
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreEventWiringRuntime(adapter, null!, Router()));
    }

    /// <summary>
    /// After the P0 contract consolidation <see cref="IDownloadAdapter"/> and
    /// <see cref="IPermissionAdapter"/> are mandatory facets of
    /// <see cref="IWebViewAdapter"/>. This stub re-declares both interfaces so it
    /// can raise the corresponding events from the test body; it is the canonical
    /// adapter used by every test in this file.
    /// </summary>
    private sealed class FullAdapterStub : StubWebViewAdapter, IDownloadAdapter, IPermissionAdapter
    {
        public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
        public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;

        public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
            => DownloadRequested?.Invoke(this, args);

        public void RaisePermissionRequested(PermissionRequestedEventArgs args)
            => PermissionRequested?.Invoke(this, args);
    }
}
