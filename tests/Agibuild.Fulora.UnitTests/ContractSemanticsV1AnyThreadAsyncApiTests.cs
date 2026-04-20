using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class ContractSemanticsV1AnyThreadAsyncApiTests
{
    [Fact]
    public void Core_async_apis_called_off_thread_execute_adapter_on_ui_thread()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        var adapter = new AnyThreadCaptureAdapter
        {
            CanGoBack = true,
            CanGoForward = true
        };
        using var core = new WebViewCore(adapter, dispatcher);

        var navigateTask = Task.Run(() => core.NavigateAsync(new Uri("https://example.test/nav")), ct);
        PumpUntilCompleted(dispatcher, navigateTask);
        navigateTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.NavigateThreadId);

        var navigateToStringTask = Task.Run(() => core.NavigateToStringAsync("<html><body>ok</body></html>"), ct);
        PumpUntilCompleted(dispatcher, navigateToStringTask);
        navigateToStringTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.NavigateToStringThreadId);

        var invokeScriptTask = Task.Run(() => core.InvokeScriptAsync("1+1"), ct);
        PumpUntilCompleted(dispatcher, invokeScriptTask);
        Assert.Equal("ok", invokeScriptTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.InvokeScriptThreadId);

        var goBackTask = Task.Run(() => core.GoBackAsync(), ct);
        PumpUntilCompleted(dispatcher, goBackTask);
        Assert.True(goBackTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.GoBackThreadId);

        var goForwardTask = Task.Run(() => core.GoForwardAsync(), ct);
        PumpUntilCompleted(dispatcher, goForwardTask);
        Assert.True(goForwardTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.GoForwardThreadId);

        var refreshTask = Task.Run(() => core.RefreshAsync(), ct);
        PumpUntilCompleted(dispatcher, refreshTask);
        Assert.True(refreshTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.RefreshThreadId);

        adapter.AutoCompleteNavigation = false;
        var pendingNavigationTask = Task.Run(() => core.NavigateAsync(new Uri("https://example.test/pending")), ct);
        ThreadingTestHelper.PumpUntil(dispatcher, () => adapter.HasPendingNavigation);

        var stopTask = Task.Run(() => core.StopAsync(), ct);
        PumpUntilCompleted(dispatcher, stopTask);
        Assert.True(stopTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.StopThreadId);

        pendingNavigationTask.GetAwaiter().GetResult();
    }

    [Fact]
    public void Feature_async_apis_called_off_thread_execute_adapter_on_ui_thread()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        var adapter = new AnyThreadCaptureAdapter();
        using var core = new WebViewCore(adapter, dispatcher);

        var openDevToolsTask = Task.Run(() => core.OpenDevToolsAsync(), ct);
        PumpUntilCompleted(dispatcher, openDevToolsTask);
        openDevToolsTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.OpenDevToolsThreadId);

        var closeDevToolsTask = Task.Run(() => core.CloseDevToolsAsync(), ct);
        PumpUntilCompleted(dispatcher, closeDevToolsTask);
        closeDevToolsTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.CloseDevToolsThreadId);

        var isDevToolsOpenTask = Task.Run(() => core.IsDevToolsOpenAsync(), ct);
        PumpUntilCompleted(dispatcher, isDevToolsOpenTask);
        Assert.False(isDevToolsOpenTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.IsDevToolsOpenThreadId);

        var screenshotTask = Task.Run(() => core.CaptureScreenshotAsync(), ct);
        PumpUntilCompleted(dispatcher, screenshotTask);
        Assert.NotEmpty(screenshotTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.CaptureScreenshotThreadId);

        var printTask = Task.Run(() => core.PrintToPdfAsync(new PdfPrintOptions()), ct);
        PumpUntilCompleted(dispatcher, printTask);
        Assert.NotEmpty(printTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.PrintToPdfThreadId);

        var getZoomTask = Task.Run(() => core.GetZoomFactorAsync(), ct);
        PumpUntilCompleted(dispatcher, getZoomTask);
        Assert.Equal(1.0, getZoomTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.GetZoomThreadId);

        var setZoomTask = Task.Run(() => core.SetZoomFactorAsync(1.25), ct);
        PumpUntilCompleted(dispatcher, setZoomTask);
        setZoomTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.SetZoomThreadId);

        var findTask = Task.Run(() => core.FindInPageAsync("hello"), ct);
        PumpUntilCompleted(dispatcher, findTask);
        Assert.Equal(3, findTask.GetAwaiter().GetResult().TotalMatches);
        Assert.Equal(dispatcher.UiThreadId, adapter.FindThreadId);

        var stopFindTask = Task.Run(() => core.StopFindInPageAsync(clearHighlights: false), ct);
        PumpUntilCompleted(dispatcher, stopFindTask);
        stopFindTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.StopFindThreadId);

        var addPreloadTask = Task.Run(() => core.AddPreloadScriptAsync("console.log('x')"), ct);
        PumpUntilCompleted(dispatcher, addPreloadTask);
        var preloadId = addPreloadTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.AddPreloadThreadId);

        var removePreloadTask = Task.Run(() => core.RemovePreloadScriptAsync(preloadId), ct);
        PumpUntilCompleted(dispatcher, removePreloadTask);
        removePreloadTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.RemovePreloadThreadId);
    }

    [Fact]
    public void Manager_async_apis_called_off_thread_execute_adapter_on_ui_thread()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new TestDispatcher();
        var adapter = new AnyThreadCaptureAdapter();
        using var core = new WebViewCore(adapter, dispatcher);

        var commandManager = core.TryGetCommandManager();
        Assert.NotNull(commandManager);

        var cookieManager = core.TryGetCookieManager();
        Assert.NotNull(cookieManager);

        var copyTask = Task.Run(() => commandManager!.CopyAsync(), ct);
        PumpUntilCompleted(dispatcher, copyTask);
        copyTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.LastCommandThreadId);

        var setCookieTask = Task.Run(
            () => cookieManager!.SetCookieAsync(new WebViewCookie("k", "v", ".example.test", "/", null, false, false)),
            ct);
        PumpUntilCompleted(dispatcher, setCookieTask);
        setCookieTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.SetCookieThreadId);

        var getCookiesTask = Task.Run(() => cookieManager!.GetCookiesAsync(new Uri("https://example.test/")), ct);
        PumpUntilCompleted(dispatcher, getCookiesTask);
        Assert.Single(getCookiesTask.GetAwaiter().GetResult());
        Assert.Equal(dispatcher.UiThreadId, adapter.GetCookiesThreadId);

        var deleteCookieTask = Task.Run(
            () => cookieManager!.DeleteCookieAsync(new WebViewCookie("k", "v", ".example.test", "/", null, false, false)),
            ct);
        PumpUntilCompleted(dispatcher, deleteCookieTask);
        deleteCookieTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.DeleteCookieThreadId);

        var clearCookiesTask = Task.Run(() => cookieManager!.ClearAllCookiesAsync(), ct);
        PumpUntilCompleted(dispatcher, clearCookiesTask);
        clearCookiesTask.GetAwaiter().GetResult();
        Assert.Equal(dispatcher.UiThreadId, adapter.ClearCookiesThreadId);
    }

    private static void PumpUntilCompleted(TestDispatcher dispatcher, Task task)
    {
        // Use DispatcherTestPump's default timeout (60s). The previous 10s bound was tight enough
        // that busy macOS-hosted runners occasionally deadlined inner Task.Run → dispatcher → adapter
        // hops that complete in sub-second time locally. 60s still surfaces a real hang quickly
        // (each pump cycle is a 50ms slice) without flaking under parallel CI load.
        ThreadingTestHelper.PumpUntil(dispatcher, () => task.IsCompleted);
        dispatcher.RunAll();
    }

    private sealed class AnyThreadCaptureAdapter :
        StubWebViewAdapter,
        IDevToolsAdapter,
        IScreenshotAdapter,
        IPrintAdapter,
        IZoomAdapter,
        IFindInPageAdapter,
        IPreloadScriptAdapter,
        ICommandAdapter,
        ICookieAdapter
    {
        private bool _initialized;
        private readonly Dictionary<string, WebViewCookie> _cookies = new();
        private readonly Dictionary<string, string> _preloadScripts = new();
        private int _preloadIdCounter;
        private Guid? _pendingNavigationId;
        private Uri? _pendingNavigationUri;
        private double _zoomFactor = 1.0;

        public bool AutoCompleteNavigation { get; set; } = true;
        public bool HasPendingNavigation => _pendingNavigationId.HasValue;

        public int? NavigateThreadId { get; private set; }
        public int? NavigateToStringThreadId { get; private set; }
        public int? InvokeScriptThreadId { get; private set; }
        public int? GoBackThreadId { get; private set; }
        public int? GoForwardThreadId { get; private set; }
        public int? RefreshThreadId { get; private set; }
        public int? StopThreadId { get; private set; }
        public int? OpenDevToolsThreadId { get; private set; }
        public int? CloseDevToolsThreadId { get; private set; }
        public int? IsDevToolsOpenThreadId { get; private set; }
        public int? CaptureScreenshotThreadId { get; private set; }
        public int? PrintToPdfThreadId { get; private set; }
        public int? GetZoomThreadId { get; private set; }
        public int? SetZoomThreadId { get; private set; }
        public int? FindThreadId { get; private set; }
        public int? StopFindThreadId { get; private set; }
        public int? AddPreloadThreadId { get; private set; }
        public int? RemovePreloadThreadId { get; private set; }
        public int? LastCommandThreadId { get; private set; }
        public int? GetCookiesThreadId { get; private set; }
        public int? SetCookieThreadId { get; private set; }
        public int? DeleteCookieThreadId { get; private set; }
        public int? ClearCookiesThreadId { get; private set; }

        public event EventHandler<double>? ZoomFactorChanged;

        public override void Initialize(IWebViewAdapterHost host)
        {
            _initialized = true;
        }

        public override void Attach(INativeHandle parentHandle)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Adapter must be initialized before attach.");
            }
        }

        public override void Detach()
        {
        }

        public override Task NavigateAsync(Guid navigationId, Uri uri)
        {
            NavigateThreadId = Environment.CurrentManagedThreadId;
            _pendingNavigationId = navigationId;
            _pendingNavigationUri = uri;
            if (AutoCompleteNavigation)
            {
                CompletePendingNavigation(NavigationCompletedStatus.Success, error: null);
            }

            return Task.CompletedTask;
        }

        public override Task NavigateToStringAsync(Guid navigationId, string html)
            => NavigateToStringAsync(navigationId, html, baseUrl: null);

        public override Task NavigateToStringAsync(Guid navigationId, string html, Uri? baseUrl)
        {
            NavigateToStringThreadId = Environment.CurrentManagedThreadId;
            _pendingNavigationId = navigationId;
            _pendingNavigationUri = baseUrl ?? new Uri("about:blank");
            if (AutoCompleteNavigation)
            {
                CompletePendingNavigation(NavigationCompletedStatus.Success, error: null);
            }

            return Task.CompletedTask;
        }

        public override Task<string?> InvokeScriptAsync(string script)
        {
            InvokeScriptThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult<string?>("ok");
        }

        public override bool GoBack(Guid navigationId)
        {
            GoBackThreadId = Environment.CurrentManagedThreadId;
            return true;
        }

        public override bool GoForward(Guid navigationId)
        {
            GoForwardThreadId = Environment.CurrentManagedThreadId;
            return true;
        }

        public override bool Refresh(Guid navigationId)
        {
            RefreshThreadId = Environment.CurrentManagedThreadId;
            return true;
        }

        public override bool Stop()
        {
            StopThreadId = Environment.CurrentManagedThreadId;
            if (_pendingNavigationId.HasValue && _pendingNavigationUri is not null)
            {
                CompletePendingNavigation(NavigationCompletedStatus.Canceled, error: null);
            }

            return true;
        }

        public void OpenDevTools()
        {
            OpenDevToolsThreadId = Environment.CurrentManagedThreadId;
        }

        public void CloseDevTools()
        {
            CloseDevToolsThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsDevToolsOpen
        {
            get
            {
                IsDevToolsOpenThreadId = Environment.CurrentManagedThreadId;
                return false;
            }
        }

        public Task<byte[]> CaptureScreenshotAsync()
        {
            CaptureScreenshotThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        }

        public Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options)
        {
            PrintToPdfThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        }

        public double ZoomFactor
        {
            get
            {
                GetZoomThreadId = Environment.CurrentManagedThreadId;
                return _zoomFactor;
            }
            set
            {
                SetZoomThreadId = Environment.CurrentManagedThreadId;
                _zoomFactor = value;
                ZoomFactorChanged?.Invoke(this, value);
            }
        }

        public Task<FindInPageEventArgs> FindAsync(string text, FindInPageOptions? options)
        {
            FindThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult(new FindInPageEventArgs
            {
                ActiveMatchIndex = 0,
                TotalMatches = 3
            });
        }

        public void StopFind(bool clearHighlights = true)
        {
            StopFindThreadId = Environment.CurrentManagedThreadId;
        }

        public string AddPreloadScript(string javaScript)
        {
            AddPreloadThreadId = Environment.CurrentManagedThreadId;
            var id = $"script_{Interlocked.Increment(ref _preloadIdCounter)}";
            _preloadScripts[id] = javaScript;
            return id;
        }

        public void RemovePreloadScript(string scriptId)
        {
            RemovePreloadThreadId = Environment.CurrentManagedThreadId;
            _preloadScripts.Remove(scriptId);
        }

        public void ExecuteCommand(WebViewCommand command)
        {
            LastCommandThreadId = Environment.CurrentManagedThreadId;
        }

        public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
        {
            GetCookiesThreadId = Environment.CurrentManagedThreadId;
            var host = uri.Host;
            var list = _cookies.Values
                .Where(c => c.Domain.EndsWith(host, StringComparison.OrdinalIgnoreCase) || host.EndsWith(c.Domain, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<WebViewCookie>>(list);
        }

        public Task SetCookieAsync(WebViewCookie cookie)
        {
            SetCookieThreadId = Environment.CurrentManagedThreadId;
            _cookies[$"{cookie.Name}|{cookie.Domain}|{cookie.Path}"] = cookie;
            return Task.CompletedTask;
        }

        public Task DeleteCookieAsync(WebViewCookie cookie)
        {
            DeleteCookieThreadId = Environment.CurrentManagedThreadId;
            _cookies.Remove($"{cookie.Name}|{cookie.Domain}|{cookie.Path}");
            return Task.CompletedTask;
        }

        public Task ClearAllCookiesAsync()
        {
            ClearCookiesThreadId = Environment.CurrentManagedThreadId;
            _cookies.Clear();
            return Task.CompletedTask;
        }

        private void CompletePendingNavigation(NavigationCompletedStatus status, Exception? error)
        {
            if (_pendingNavigationId is null || _pendingNavigationUri is null)
            {
                return;
            }

            RaiseNavigationCompleted(new NavigationCompletedEventArgs(
                _pendingNavigationId.Value,
                _pendingNavigationUri,
                status,
                error));

            _pendingNavigationId = null;
            _pendingNavigationUri = null;
        }
    }
}
