using Agibuild.Fulora.Adapters.Abstractions;
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
            AdapterCapabilities.From(adapter),
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
            AdapterCapabilities.From(adapter),
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
    public void Core_only_adapter_skips_optional_download_and_permission_subscriptions()
    {
        // When the adapter does not implement IDownloadAdapter/IPermissionAdapter, wiring must
        // skip those subscriptions silently. Dispose must still succeed and not attempt to
        // unhook handlers that were never attached.
        var download = 0;
        var permission = 0;
        var adapter = new CoreOnlyAdapterStub();

        var runtime = new WebViewCoreEventWiringRuntime(
            adapter,
            AdapterCapabilities.From(adapter),
            Logger(),
            Router(
                onDownloadRequested: _ => download++,
                onPermissionRequested: _ => permission++));

        runtime.Dispose();

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
                AdapterCapabilities.From(adapter),
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
        var adapter = new FullAdapterStub();
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreEventWiringRuntime(null!, AdapterCapabilities.From(adapter), Logger(), Router()));
    }

    [Fact]
    public void Constructor_rejects_null_logger()
    {
        var adapter = new FullAdapterStub();
        Assert.Throws<ArgumentNullException>(() =>
            new WebViewCoreEventWiringRuntime(adapter, AdapterCapabilities.From(adapter), null!, Router()));
    }

#pragma warning disable CS0067 // Events that are unused on the stub's Raise path but required by the interface contract.
    private sealed class CoreOnlyAdapterStub : IWebViewAdapter
    {
        public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
        public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
        public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
        public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
        public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;

        public void Initialize(IWebViewAdapterHost host) { }
        public void Attach(INativeHandle parentHandle) { }
        public void Detach() { }

        public Task NavigateAsync(Guid navigationId, Uri uri) => Task.CompletedTask;
        public Task NavigateToStringAsync(Guid navigationId, string html) => Task.CompletedTask;
        public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl) => Task.CompletedTask;
        public Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);

        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool GoBack(Guid navigationId) => false;
        public bool GoForward(Guid navigationId) => false;
        public bool Refresh(Guid navigationId) => false;
        public bool Stop() => false;
    }

    private sealed class FullAdapterStub : IWebViewAdapter, IDownloadAdapter, IPermissionAdapter
    {
        public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted;
        public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
        public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived;
        public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;
        public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
        public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
        public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;

        public void Initialize(IWebViewAdapterHost host) { }
        public void Attach(INativeHandle parentHandle) { }
        public void Detach() { }

        public Task NavigateAsync(Guid navigationId, Uri uri) => Task.CompletedTask;
        public Task NavigateToStringAsync(Guid navigationId, string html) => Task.CompletedTask;
        public Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl) => Task.CompletedTask;
        public Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);

        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool GoBack(Guid navigationId) => false;
        public bool GoForward(Guid navigationId) => false;
        public bool Refresh(Guid navigationId) => false;
        public bool Stop() => false;

        public void RaiseNavigationCompleted(NavigationCompletedEventArgs args)
            => NavigationCompleted?.Invoke(this, args);

        public void RaiseNewWindowRequested(NewWindowRequestedEventArgs args)
            => NewWindowRequested?.Invoke(this, args);

        public void RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
            => WebMessageReceived?.Invoke(this, args);

        public void RaiseWebResourceRequested(WebResourceRequestedEventArgs args)
            => WebResourceRequested?.Invoke(this, args);

        public void RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args)
            => EnvironmentRequested?.Invoke(this, args);

        public void RaiseDownloadRequested(DownloadRequestedEventArgs args)
            => DownloadRequested?.Invoke(this, args);

        public void RaisePermissionRequested(PermissionRequestedEventArgs args)
            => PermissionRequested?.Invoke(this, args);
    }
#pragma warning restore CS0067
}
