using System.Threading;
using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Contract tests for WebAuthBroker — verifies the full OAuth authentication flow
/// using mock dialogs (success, user cancel, timeout, validation).
/// </summary>
public sealed class WebAuthBrokerTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void Success_flow_returns_callback_uri()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();
        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://auth.example.com/authorize?client_id=test"),
            CallbackUri = new Uri("myapp://auth/callback"),
        };

        // The mock adapter auto-completes NavigateAsync.
        // After initial navigation completes, simulate the OAuth redirect to callback.
        factory.OnDialogCreated = (dialog, adapter) =>
        {
            adapter.AutoCompleteNavigation = true;
            adapter.OnNavigationAutoCompleted = () =>
            {
                // Simulate the OAuth provider redirecting to the callback URI.
                _ = adapter.SimulateNativeNavigationStartingAsync(
                    new Uri("myapp://auth/callback?code=abc123"));
            };
        };

        var result = DispatcherTestPump.Run(_dispatcher, () => broker.AuthenticateAsync(owner, options), TimeSpan.FromSeconds(20));

        Assert.Equal(WebAuthStatus.Success, result.Status);
        Assert.NotNull(result.CallbackUri);
        Assert.StartsWith("myapp://auth/callback", result.CallbackUri!.AbsoluteUri);
    }

    [Fact]
    public void Authorize_uri_already_matching_callback_returns_success_deterministically()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();
        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://example.com/callback?code=simulated123&state=abc"),
            CallbackUri = new Uri("https://example.com/callback"),
        };

        // Keep adapter behavior safe if navigation falls through unexpectedly.
        factory.OnDialogCreated = (_, adapter) => adapter.AutoCompleteNavigation = true;

        var result = DispatcherTestPump.Run(_dispatcher, () => broker.AuthenticateAsync(owner, options), TimeSpan.FromSeconds(10));

        Assert.Equal(WebAuthStatus.Success, result.Status);
        Assert.NotNull(result.CallbackUri);
        Assert.Contains("code=simulated123", result.CallbackUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void User_cancel_returns_UserCancel()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();
        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://auth.example.com/authorize"),
            CallbackUri = new Uri("myapp://auth/callback"),
        };

        // After initial navigation completes, simulate user closing dialog.
        factory.OnDialogCreated = (dialog, adapter) =>
        {
            adapter.AutoCompleteNavigation = true;
            adapter.OnNavigationAutoCompleted = () =>
            {
                factory.LastHost?.SimulateUserClose();
            };
        };

        var result = DispatcherTestPump.Run(_dispatcher, () => broker.AuthenticateAsync(owner, options), TimeSpan.FromSeconds(10));

        Assert.Equal(WebAuthStatus.UserCancel, result.Status);
    }

    [Fact]
    public void Timeout_returns_Timeout()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();
        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://auth.example.com/authorize"),
            CallbackUri = new Uri("myapp://auth/callback"),
            Timeout = TimeSpan.FromMilliseconds(100),
        };

        // Navigate completes but no callback redirect — let timeout fire.
        factory.OnDialogCreated = (dialog, adapter) =>
        {
            adapter.AutoCompleteNavigation = true;
        };

        var result = DispatcherTestPump.Run(_dispatcher, () => broker.AuthenticateAsync(owner, options), TimeSpan.FromSeconds(10));

        Assert.Equal(WebAuthStatus.Timeout, result.Status);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Missing_AuthorizeUri_throws()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();

        var options = new AuthOptions
        {
            CallbackUri = new Uri("myapp://auth/callback"),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => broker.AuthenticateAsync(owner, options));
    }

    [Fact]
    public async Task Missing_CallbackUri_throws()
    {
        var factory = new AuthTestDialogFactory(_dispatcher);
        var broker = new WebAuthBroker(factory);
        var owner = new DummyTopLevelWindow();

        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://auth.example.com/authorize"),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => broker.AuthenticateAsync(owner, options));
    }

    [Fact]
    public void Constructor_null_factory_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebAuthBroker(null!));
    }

    [Fact]
    public async Task AuthenticateAsync_keeps_captured_sync_context_for_dialog_cleanup()
    {
        var originalContext = SynchronizationContext.Current;
        var markerContext = new InlineTrackingSynchronizationContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(markerContext);

            var callbackUri = new Uri("myapp://auth/callback?code=ctx");
            var dialog = new ContextAwareAuthDialog(markerContext, callbackUri);
            var broker = new WebAuthBroker(new SingleDialogFactory(dialog));
            var owner = new DummyTopLevelWindow();
            var options = new AuthOptions
            {
                AuthorizeUri = new Uri("https://auth.example.com/authorize"),
                CallbackUri = new Uri("myapp://auth/callback"),
            };

            var result = await broker.AuthenticateAsync(owner, options);

            Assert.Equal(WebAuthStatus.Success, result.Status);
            Assert.Equal(callbackUri, result.CallbackUri);
            Assert.True(dialog.CleanupExecutedOnExpectedContext);
            Assert.True(markerContext.PostCount > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    // ---- Test helpers ----

    private sealed class DummyTopLevelWindow : ITopLevelWindow
    {
        public INativeHandle? PlatformHandle => null;
    }

    /// <summary>
    /// Auth-specific dialog factory that creates real WebDialog instances backed by mocks,
    /// and allows test code to hook into the dialog lifecycle (e.g., simulate redirects).
    /// </summary>
    private sealed class AuthTestDialogFactory : IWebDialogFactory
    {
        private readonly TestDispatcher _dispatcher;

        public AuthTestDialogFactory(TestDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public MockDialogHost? LastHost { get; private set; }

        /// <summary>
        /// Called after a dialog is created. Test code can hook into adapter events here
        /// to simulate OAuth redirects, user actions, etc.
        /// </summary>
        public Action<WebDialog, MockWebViewAdapter>? OnDialogCreated { get; set; }

        public IWebDialog Create(IWebViewEnvironmentOptions? options = null)
        {
            var host = new MockDialogHost();
            var adapter = MockWebViewAdapter.Create();
            var dialog = new WebDialog(host, adapter, _dispatcher);
            LastHost = host;

            OnDialogCreated?.Invoke(dialog, adapter);
            return dialog;
        }
    }

    private sealed class SingleDialogFactory(IWebDialog dialog) : IWebDialogFactory
    {
        public IWebDialog Create(IWebViewEnvironmentOptions? options = null) => dialog;
    }

    private sealed class ContextAwareAuthDialog : IWebDialog
    {
        private readonly SynchronizationContext _expectedContext;
        private readonly Uri _callbackUri;
        private readonly IBridgeService _bridge = new StubBridgeService();

        public ContextAwareAuthDialog(SynchronizationContext expectedContext, Uri callbackUri)
        {
            _expectedContext = expectedContext;
            _callbackUri = callbackUri;
        }

        public bool CleanupExecutedOnExpectedContext { get; private set; }

        public string? Title { get; set; }
        public bool CanUserResize { get; set; }
        public Uri Source { get; set; } = new("about:blank");
        public bool CanGoBack => false;
        public bool CanGoForward => false;
        public bool IsLoading => false;
        public Guid ChannelId { get; } = Guid.NewGuid();
        public IWebViewRpcService? Rpc => null;
        public IBridgeTracer? BridgeTracer { get; set; }
        public IBridgeService Bridge => _bridge;

        public void Show() { }
        public bool Show(INativeHandle owner) => true;

        public Task NavigateAsync(Uri uri)
        {
            _ = Task.Run(() =>
            {
                NavigationStarted?.Invoke(this, new NavigationStartingEventArgs(_callbackUri));
            });
            return Task.CompletedTask;
        }

        public Task NavigateToStringAsync(string html) => Task.CompletedTask;
        public Task NavigateToStringAsync(string html, Uri? baseUrl) => Task.CompletedTask;
        public Task<string?> InvokeScriptAsync(string script) => Task.FromResult<string?>(null);
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

        public void Close()
        {
            CleanupExecutedOnExpectedContext = ReferenceEquals(SynchronizationContext.Current, _expectedContext);
        }

        public bool Resize(int width, int height) => true;
        public bool Move(int x, int y) => true;
        public void Dispose() { }

#pragma warning disable CS0067 // Interface-required events not raised in test stub
        public event EventHandler? Closing;
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
        public event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
#pragma warning restore CS0067
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

    private sealed class StubBridgeService : IBridgeService
    {
        public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class { }
        public T GetProxy<T>() where T : class => throw new NotSupportedException();
        public void Remove<T>() where T : class { }
    }
}
