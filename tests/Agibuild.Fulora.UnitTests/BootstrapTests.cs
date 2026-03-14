using Agibuild.Fulora;
using Agibuild.Fulora.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class BootstrapTests
{
    [Fact]
    public async Task BootstrapSpaAsync_DevMode_navigates_to_dev_server_url()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://localhost:5173"), webView.LastNavigatedUri);
    }

    [Fact]
    public async Task BootstrapSpaAsync_ProdMode_navigates_to_app_scheme()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("app://localhost/index.html"), webView.LastNavigatedUri);
    }

    [Fact]
    public async Task BootstrapSpaAsync_CustomScheme_uses_provided_scheme()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            Scheme = "custom",
            FallbackDocument = "main.html"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("custom://localhost/main.html"), webView.LastNavigatedUri);
    }

    [Fact]
    public async Task BootstrapSpaAsync_ProdMode_with_embedded_options_enables_spa_hosting()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            EmbeddedResourcePrefix = "wwwroot",
            ResourceAssembly = typeof(BootstrapTests).Assembly,
            FallbackDocument = "index.html"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, webView.EnableSpaHostingCallCount);
        Assert.NotNull(webView.LastSpaHostingOptions);
        Assert.Equal("wwwroot", webView.LastSpaHostingOptions!.EmbeddedResourcePrefix);
        Assert.Equal(typeof(BootstrapTests).Assembly, webView.LastSpaHostingOptions.ResourceAssembly);
    }

    [Fact]
    public async Task BootstrapSpaAsync_throws_when_embedded_options_are_partial()
    {
        var webView = new TrackingWebView();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            webView.BootstrapSpaAsync(new SpaBootstrapOptions
            {
                EmbeddedResourcePrefix = "wwwroot"
            }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BootstrapSpaAsync_invokes_ConfigureBridge_after_navigation()
    {
        var webView = new TrackingWebView();
        var bridgeConfigured = false;
        Uri? uriAtConfigureTime = null;

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173",
            ConfigureBridge = (bridge, sp) =>
            {
                uriAtConfigureTime = webView.LastNavigatedUri;
                bridgeConfigured = true;
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(bridgeConfigured);
        Assert.Equal(new Uri("http://localhost:5173"), uriAtConfigureTime);
    }

    [Fact]
    public async Task BootstrapSpaAsync_preserves_calling_synchronization_context_for_bridge_setup()
    {
        var originalContext = SynchronizationContext.Current;
        var markerContext = new InlineTrackingSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(markerContext);

            var webView = new TrackingWebView
            {
                NavigateCompletesAsynchronously = true,
                BridgeAccessValidator = () => ReferenceEquals(SynchronizationContext.Current, markerContext)
            };

            var bridgeConfigured = false;
            await webView.BootstrapSpaAsync(new SpaBootstrapOptions
            {
                DevServerUrl = "http://localhost:5173",
                ConfigureBridge = (_, _) => bridgeConfigured = true
            }, TestContext.Current.CancellationToken);

            Assert.True(bridgeConfigured);
            Assert.True(markerContext.PostCount > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task BootstrapSpaAsync_dispatches_ready_event_script()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173"
        }, TestContext.Current.CancellationToken);

        Assert.Contains("agWebViewReady", webView.LastScript!);
        Assert.Contains("__agWebViewReady", webView.LastScript!);
    }

    [Fact]
    public async Task BootstrapSpaAsync_navigation_error_shows_error_page()
    {
        var webView = new TrackingWebView { NavigateThrows = true };

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173"
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(webView.LastHtmlContent);
        Assert.Contains("Navigation failed", webView.LastHtmlContent);
    }

    [Fact]
    public async Task BootstrapSpaAsync_navigation_error_uses_custom_error_factory()
    {
        var webView = new TrackingWebView { NavigateThrows = true };

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173",
            ErrorPageFactory = ex => $"<h1>Custom: {ex.Message}</h1>"
        }, TestContext.Current.CancellationToken);

        Assert.Contains("Custom:", webView.LastHtmlContent!);
    }

    [Fact]
    public async Task BootstrapSpaAsync_passes_ServiceProvider_to_ConfigureBridge()
    {
        var webView = new TrackingWebView();
        var capturedSp = (IServiceProvider?)null;
        var mockSp = new MinimalServiceProvider();

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173",
            ServiceProvider = mockSp,
            ConfigureBridge = (bridge, sp) => { capturedSp = sp; }
        }, TestContext.Current.CancellationToken);

        Assert.Same(mockSp, capturedSp);
    }

    [Fact]
    public async Task BootstrapSpaAsync_throws_on_null_webView()
    {
        IWebView webView = null!;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            webView.BootstrapSpaAsync(new SpaBootstrapOptions(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BootstrapSpaAsync_throws_on_null_options()
    {
        var webView = new TrackingWebView();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            webView.BootstrapSpaAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BootstrapSpaAsync_navigation_error_does_not_invoke_ConfigureBridge()
    {
        var webView = new TrackingWebView { NavigateThrows = true };
        var bridgeConfigured = false;

        await webView.BootstrapSpaAsync(new SpaBootstrapOptions
        {
            DevServerUrl = "http://localhost:5173",
            ConfigureBridge = (_, _) => { bridgeConfigured = true; }
        }, TestContext.Current.CancellationToken);

        Assert.False(bridgeConfigured);
    }

    [Fact]
    public async Task BootstrapSpaAsync_DI_overload_applies_registered_bridge_actions()
    {
        var webView = new TrackingWebView();
        var actionInvoked = false;

        var services = new ServiceCollection();
        services.AddFulora().ConfigureBridge((bridge, _) => { actionInvoked = true; });
        var sp = services.BuildServiceProvider();

        await webView.BootstrapSpaAsync(
            new SpaBootstrapOptions { DevServerUrl = "http://localhost:5173" },
            sp,
            TestContext.Current.CancellationToken);

        Assert.True(actionInvoked);
        Assert.Equal(new Uri("http://localhost:5173"), webView.LastNavigatedUri);
    }

    [Fact]
    public async Task BootstrapSpaAsync_DI_overload_composes_explicit_and_di_actions()
    {
        var webView = new TrackingWebView();
        var order = new List<string>();

        var services = new ServiceCollection();
        services.AddFulora().ConfigureBridge((bridge, _) => order.Add("di"));
        var sp = services.BuildServiceProvider();

        await webView.BootstrapSpaAsync(
            new SpaBootstrapOptions
            {
                DevServerUrl = "http://localhost:5173",
                ConfigureBridge = (_, _) => order.Add("explicit")
            },
            sp,
            TestContext.Current.CancellationToken);

        Assert.Equal(["di", "explicit"], order);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_uses_wrapped_bootstrap_options()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
                    DevServerUrl = "http://localhost:5173"
                }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://localhost:5173"), webView.LastNavigatedUri);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_uses_embedded_hosting_when_dev_server_not_configured()
    {
        var webView = new TrackingWebView();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(BootstrapTests).Assembly,
                    FallbackDocument = "index.html"
                }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("app://localhost/index.html"), webView.LastNavigatedUri);
        Assert.Equal(1, webView.EnableSpaHostingCallCount);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_applies_extensions_after_baseline_configuration()
    {
        var webView = new TrackingWebView();
        var order = new List<string>();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                ExceptionScope = new SpaBootstrapProfileExceptionScope { ScopeId = "scope-1" },
                BootstrapOptions = new SpaBootstrapOptions
                {
                    DevServerUrl = "http://localhost:5173",
                    ConfigureBridge = (_, _) => order.Add("baseline")
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "ext-1",
                        Configure = (_, _, scope) => order.Add($"ext-1:{scope?.ScopeId}")
                    },
                    new SpaBootstrapProfileExtension
                    {
                        Id = "ext-2",
                        Configure = (_, _, scope) => order.Add($"ext-2:{scope?.ScopeId}")
                    }
                ]
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["baseline", "ext-1:scope-1", "ext-2:scope-1"], order);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_DI_overload_composes_di_baseline_and_extensions()
    {
        var webView = new TrackingWebView();
        var order = new List<string>();
        var services = new ServiceCollection();
        services.AddFulora().ConfigureBridge((_, _) => order.Add("di"));
        var sp = services.BuildServiceProvider();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
                    DevServerUrl = "http://localhost:5173",
                    ConfigureBridge = (_, _) => order.Add("baseline")
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "ext-1",
                        Configure = (_, _, _) => order.Add("ext-1")
                    }
                ]
            },
            sp,
            TestContext.Current.CancellationToken);

        Assert.Equal(["di", "baseline", "ext-1"], order);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_DI_overload_keeps_order_stable_across_repeated_runs()
    {
        var runs = new List<string[]>();

        for (var i = 0; i < 3; i++)
        {
            var webView = new TrackingWebView();
            var order = new List<string>();
            var services = new ServiceCollection();
            services.AddFulora().ConfigureBridge((_, _) => order.Add("di"));
            var sp = services.BuildServiceProvider();

            await webView.BootstrapSpaProfileAsync(
                new SpaBootstrapProfileOptions
                {
                    BootstrapOptions = new SpaBootstrapOptions
                    {
                        DevServerUrl = "http://localhost:5173",
                        ConfigureBridge = (_, _) => order.Add("baseline")
                    },
                    Extensions =
                    [
                        new SpaBootstrapProfileExtension
                        {
                            Id = "ext-1",
                            Configure = (_, _, _) => order.Add("ext-1")
                        }
                    ]
                },
                sp,
                TestContext.Current.CancellationToken);

            runs.Add([.. order]);
        }

        Assert.All(runs, run => Assert.Equal(["di", "baseline", "ext-1"], run));
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_runs_profile_teardowns_exactly_once_on_dispose()
    {
        var webView = new TrackingWebView();
        var order = new List<string>();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                ExceptionScope = new SpaBootstrapProfileExceptionScope { ScopeId = "scope-1" },
                BootstrapOptions = new SpaBootstrapOptions
                {
                    DevServerUrl = "http://localhost:5173"
                },
                Teardowns =
                [
                    new SpaBootstrapProfileTeardown
                    {
                        Id = "td-1",
                        Execute = (_, scope) => order.Add($"td-1:{scope?.ScopeId}")
                    },
                    new SpaBootstrapProfileTeardown
                    {
                        Id = "td-2",
                        Execute = (_, scope) => order.Add($"td-2:{scope?.ScopeId}")
                    }
                ]
            },
            TestContext.Current.CancellationToken);

        webView.Dispose();
        webView.Dispose();

        Assert.Equal(["td-1:scope-1", "td-2:scope-1"], order);
    }

    [Fact]
    public async Task BootstrapSpaProfileAsync_DI_overload_runs_profile_teardown_once_with_service_provider()
    {
        var webView = new TrackingWebView();
        var capture = new List<string>();
        var marker = new object();
        var services = new ServiceCollection();
        services.AddSingleton(marker);
        services.AddFulora();
        var sp = services.BuildServiceProvider();

        await webView.BootstrapSpaProfileAsync(
            new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
                    DevServerUrl = "http://localhost:5173"
                },
                Teardowns =
                [
                    new SpaBootstrapProfileTeardown
                    {
                        Id = "td-1",
                        Execute = (provider, _) =>
                        {
                            var resolved = provider?.GetService(typeof(object));
                            capture.Add(ReferenceEquals(resolved, marker) ? "resolved" : "missing");
                        }
                    }
                ]
            },
            sp,
            TestContext.Current.CancellationToken);

        webView.Dispose();
        webView.Dispose();

        Assert.Equal(["resolved"], capture);
    }

    // ==================== Test doubles ====================

    private sealed class TrackingWebView : ISpaHostingWebView
    {
        public Uri? LastNavigatedUri { get; private set; }
        public string? LastScript { get; private set; }
        public string? LastHtmlContent { get; private set; }
        public SpaHostingOptions? LastSpaHostingOptions { get; private set; }
        public int EnableSpaHostingCallCount { get; private set; }
        public bool NavigateThrows { get; set; }
        public bool NavigateCompletesAsynchronously { get; set; }
        public Func<bool>? BridgeAccessValidator { get; set; }

        public Uri Source { get; set; } = new("about:blank");
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool IsLoading => false;
        public Guid ChannelId { get; } = Guid.NewGuid();

        public IWebViewRpcService? Rpc => null;
        public IBridgeTracer? BridgeTracer { get; set; }
        private readonly IBridgeService _bridge = new StubBridgeService();
        public IBridgeService Bridge
        {
            get
            {
                if (BridgeAccessValidator is not null && !BridgeAccessValidator())
                    throw new InvalidOperationException("Bridge accessed outside expected synchronization context.");

                return _bridge;
            }
        }

        public void EnableSpaHosting(SpaHostingOptions options)
        {
            LastSpaHostingOptions = options;
            EnableSpaHostingCallCount++;
        }

        public Task NavigateAsync(Uri uri)
        {
            if (NavigateThrows)
                throw new WebViewNavigationException("Test navigation failure", Guid.Empty, uri);

            if (NavigateCompletesAsynchronously)
                return CompleteNavigationAsync(uri);

            LastNavigatedUri = uri;
            return Task.CompletedTask;
        }

        private async Task CompleteNavigationAsync(Uri uri)
        {
            await Task.Delay(1);
            LastNavigatedUri = uri;
        }

        public Task NavigateToStringAsync(string html)
        {
            LastHtmlContent = html;
            return Task.CompletedTask;
        }

        public Task NavigateToStringAsync(string html, Uri? baseUrl)
        {
            LastHtmlContent = html;
            return Task.CompletedTask;
        }

        public Task<string?> InvokeScriptAsync(string script)
        {
            LastScript = script;
            return Task.FromResult<string?>(null);
        }

        public Task<bool> GoBackAsync() => Task.FromResult(false);
        public Task<bool> GoForwardAsync() => Task.FromResult(false);
        public Task<bool> RefreshAsync() => Task.FromResult(false);
        public Task<bool> StopAsync() => Task.FromResult(false);
        public ICookieManager? TryGetCookieManager() => null;
        public ICommandManager? TryGetCommandManager() => null;
        public Task<INativeHandle?> TryGetWebViewHandleAsync() => Task.FromResult<INativeHandle?>(null);
        public Task OpenDevToolsAsync() => Task.CompletedTask;
        public Task CloseDevToolsAsync() => Task.CompletedTask;
        public Task<bool> IsDevToolsOpenAsync() => Task.FromResult(false);
        public Task<byte[]> CaptureScreenshotAsync() => Task.FromResult(Array.Empty<byte>());
        public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null) => Task.FromResult(Array.Empty<byte>());
        public Task<double> GetZoomFactorAsync() => Task.FromResult(1.0);
        public Task SetZoomFactorAsync(double zoomFactor) => Task.CompletedTask;
        public Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null)
            => Task.FromResult(new FindInPageEventArgs());
        public Task StopFindInPageAsync(bool clearHighlights = true) => Task.CompletedTask;
        public Task<string> AddPreloadScriptAsync(string javaScript) => Task.FromResult("script-id");
        public Task RemovePreloadScriptAsync(string scriptId) => Task.CompletedTask;

        public event EventHandler<NavigationStartingEventArgs>? NavigationStarted { add { } remove { } }
        public event EventHandler<NavigationCompletedEventArgs>? NavigationCompleted { add { } remove { } }
        public event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested { add { } remove { } }
        public event EventHandler<WebMessageReceivedEventArgs>? WebMessageReceived { add { } remove { } }
        public event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested { add { } remove { } }
        public event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested { add { } remove { } }
        public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested { add { } remove { } }
        public event EventHandler<PermissionRequestedEventArgs>? PermissionRequested { add { } remove { } }
        public event EventHandler<AdapterCreatedEventArgs>? AdapterCreated { add { } remove { } }
        public event EventHandler? AdapterDestroyed;
        public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested { add { } remove { } }

        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            AdapterDestroyed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StubBridgeService : IBridgeService
    {
        public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class { }
        public T GetProxy<T>() where T : class => throw new NotSupportedException();
        public void Remove<T>() where T : class { }
    }

    private sealed class MinimalServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class InlineTrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;
        public int PostCount => _postCount;

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);

            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                d(state);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }

}

/// <summary>
/// Tests for bridge-managed implementation disposal (Task 4.2).
/// Uses RuntimeBridgeService directly to verify disposable implementations are cleaned up.
/// </summary>
public class BridgeDisposalTests
{
    [Fact]
    public void Dispose_disposes_IDisposable_implementations()
    {
        var bridge = CreateBridge();
        var impl = new DisposableExport();

        bridge.Expose<IMultiParamExport>(impl);
        Assert.False(impl.Disposed);

        ((IDisposable)bridge).Dispose();
        Assert.True(impl.Disposed);
    }

    [Fact]
    public void Remove_disposes_IDisposable_implementation()
    {
        var bridge = CreateBridge();
        var impl = new DisposableExport();

        bridge.Expose<IMultiParamExport>(impl);
        bridge.Remove<IMultiParamExport>();

        Assert.True(impl.Disposed);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var bridge = CreateBridge();
        var impl = new DisposableExport();

        bridge.Expose<IMultiParamExport>(impl);
        ((IDisposable)bridge).Dispose();
        ((IDisposable)bridge).Dispose();

        Assert.Equal(1, impl.DisposeCount);
    }

    private static RuntimeBridgeService CreateBridge()
    {
        var rpc = new StubRpc();
        return new RuntimeBridgeService(
            rpc,
            _ => Task.FromResult<string?>(null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    private sealed class DisposableExport : IMultiParamExport, IDisposable
    {
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<string> Greet(string name, int age, bool formal = false)
            => Task.FromResult($"Hi {name}");

        public Task VoidMethod() => Task.CompletedTask;
        public string SyncMethod(string input) => input;

        public void Dispose()
        {
            DisposeCount++;
            Disposed = true;
        }
    }

    private sealed class StubRpc : IWebViewRpcService
    {
        public void Handle(string method, Func<System.Text.Json.JsonElement?, Task<object?>> handler) { }
        public void Handle(string method, Func<System.Text.Json.JsonElement?, object?> handler) { }
        public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose) { }
        public void UnregisterHandler(string method) { }
        public Task<System.Text.Json.JsonElement> InvokeAsync(string method, object? args = null) => throw new NotSupportedException();
        public Task<T?> InvokeAsync<T>(string method, object? args = null) => throw new NotSupportedException();
    }
}
