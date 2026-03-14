using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPlatformHandle = global::Avalonia.Platform.IPlatformHandle;

namespace Agibuild.Fulora.Integration.Tests.ViewModels;

/// <summary>Auto-run mode for WebView2 smoke tests.</summary>
public enum WebView2AutoRunMode
{
    /// <summary>Standard smoke test run.</summary>
    Smoke = 0,
    /// <summary>Teardown stress test run.</summary>
    TeardownStress = 1
}

/// <summary>ViewModel for WebView2 smoke and teardown stress tests.</summary>
public partial class WebView2SmokeViewModel : ViewModelBase, IWebViewAdapterHost
{
    private readonly ConcurrentDictionary<Guid, Guid> _navigationIdByCorrelation = new();
    private readonly ConcurrentQueue<NativeNavigationStartingInfo> _nativeStarts = new();

    private INativeHandle? _hostHandle;
    private IWebViewAdapter? _adapter;
    private LoopbackHttpServer? _server;

    private TaskCompletionSource<Guid>? _nextNativeNavigationIdTcs;

    private int _autoRunStarted;
    private int _runAllInProgress;
    private readonly Action<string>? _logSink;

    /// <summary>Creates a new instance with optional log sink.</summary>
    public WebView2SmokeViewModel(Action<string>? logSink = null)
    {
        _logSink = logSink;
        ChannelId = Guid.NewGuid();
        Status = "Not started.";
    }

    /// <summary>Channel identifier for WebView messaging.</summary>
    public Guid ChannelId { get; }

    /// <summary>Whether to auto-run tests when host handle is set.</summary>
    public bool AutoRun { get; set; }

    /// <summary>Auto-run mode: smoke or teardown stress.</summary>
    public WebView2AutoRunMode AutoRunMode { get; set; } = WebView2AutoRunMode.Smoke;

    /// <summary>Number of teardown stress iterations.</summary>
    public int TeardownStressIterations { get; set; } = 10;

    /// <summary>Raised when auto-run completes with exit code.</summary>
    public event Action<int>? AutoRunCompleted;

    /// <summary>Current status text.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    [RelayCommand]
    private Task InitializeAsync()
    {
        try
        {
            LogLine("Initialize requested.");

            if (_hostHandle is null)
            {
                Status = "Waiting for native host handle...";
                return Task.CompletedTask;
            }

            EnsureServerStarted();

            if (_adapter is null)
            {
                var adapter = global::Agibuild.Fulora.WebViewAdapterFactory.CreateDefaultAdapter();
                adapter.Initialize(this);
                adapter.Attach(_hostHandle);
                _adapter = adapter;

                LogLine("WebView2 adapter attached.");
                Status = "Ready.";
                return Task.CompletedTask;
            }

            Status = "Already initialized.";
        }
        catch (Exception ex)
        {
            Status = $"Initialize failed: {ex.Message}";
            LogLine(ex.ToString());
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        _ = await RunAllCoreAsync().ConfigureAwait(false);
    }

    private async Task RunAllForAutoRunAsync()
    {
        var ok = await RunAllCoreAsync().ConfigureAwait(false);
        LogLine(ok ? "WV2 smoke: PASS" : "WV2 smoke: FAIL");
        AutoRunCompleted?.Invoke(ok ? 0 : 1);
    }

    private async Task RunTeardownStressForAutoRunAsync()
    {
        var ok = await RunTeardownStressCoreAsync().ConfigureAwait(false);
        LogLine(ok ? "WV2 teardown stress: PASS" : "WV2 teardown stress: FAIL");
        AutoRunCompleted?.Invoke(ok ? 0 : 1);
    }

    private async Task<bool> RunAllCoreAsync()
    {
        if (Interlocked.Exchange(ref _runAllInProgress, 1) != 0)
        {
            LogLine("RunAll ignored: already in progress.");
            return false;
        }

        await InitializeAsync().ConfigureAwait(false);
        if (_adapter is null || _server is null)
        {
            Interlocked.Exchange(ref _runAllInProgress, 0);
            return false;
        }

        try
        {
            Status = "Running...";

            // Wait for WebView2 to be ready (async init).
            await Task.Delay(2000).ConfigureAwait(false);

            await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

            await RunLinkClickCoreAsync().ConfigureAwait(false);
            await RunRedirect302CoreAsync().ConfigureAwait(false);
            await RunWindowLocationCoreAsync().ConfigureAwait(false);
            await RunCancelCoreAsync().ConfigureAwait(false);
            await RunScriptCoreAsync().ConfigureAwait(false);
            await RunMessageCoreAsync().ConfigureAwait(false);
            await RunCookieCrudCoreAsync().ConfigureAwait(false);
            await RunErrorCoreAsync().ConfigureAwait(false);
            await RunNativeHandleCoreAsync().ConfigureAwait(false);
            await RunBaseUrlCoreAsync().ConfigureAwait(false);

            Status = "Done.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
            LogLine(ex.ToString());
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _runAllInProgress, 0);
        }
    }

    private async Task<bool> RunTeardownStressCoreAsync()
    {
        if (Interlocked.Exchange(ref _runAllInProgress, 1) != 0)
        {
            LogLine("TeardownStress ignored: already in progress.");
            return false;
        }

        if (_hostHandle is null)
        {
            Status = "Waiting for native host handle...";
            Interlocked.Exchange(ref _runAllInProgress, 0);
            return false;
        }

        var iterations = Math.Clamp(TeardownStressIterations, 1, 50);
        EnsureServerStarted();

        try
        {
            Status = $"Teardown stress: {iterations} iteration(s)...";

            for (var i = 1; i <= iterations; i++)
            {
                Status = $"Teardown stress {i}/{iterations}: attach";

                _navigationIdByCorrelation.Clear();
                ClearNativeStarts();

                IWebViewAdapter? adapter = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    adapter = global::Agibuild.Fulora.WebViewAdapterFactory.CreateDefaultAdapter();
                    adapter.Initialize(this);
                    adapter.Attach(_hostHandle);
                    _adapter = adapter;
                });
                var adapterLocal = adapter!;

                var iterationOk = true;
                try
                {
                    // Give the native WebView2 host process a short settle window after Attach.
                    var settleDelay = i == 1
                        ? TimeSpan.FromSeconds(3)
                        : TimeSpan.FromMilliseconds(500);
                    await Task.Delay(settleDelay).ConfigureAwait(false);

                    // The first iteration can be noticeably slower on cold CI hosts because WebView2 runtime
                    // and profile bootstrap happen for the first time in the worker session.
                    var readinessTimeout = i == 1
                        ? TimeSpan.FromSeconds(90)
                        : TimeSpan.FromSeconds(20);
                    _ = await WaitAsync(adapterLocal.InvokeScriptAsync("1 + 1"), readinessTimeout).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    iterationOk = false;
                    LogLine($"Teardown stress {i}/{iterations}: readiness check failed: {ex.GetType().Name}: {ex.Message}");
                }

                Status = $"Teardown stress {i}/{iterations}: detach";
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() => adapterLocal.Detach());
                }
                catch (Exception ex)
                {
                    iterationOk = false;
                    LogLine($"Teardown stress {i}/{iterations}: detach threw: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    _adapter = null;
                }

                // Allow native teardown to settle before re-attaching on busy CI hosts.
                await Task.Delay(300).ConfigureAwait(false);

                if (!iterationOk)
                {
                    return false;
                }
            }

            Status = "Teardown stress: Done.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Teardown stress failed: {ex.Message}";
            LogLine(ex.ToString());
            return false;
        }
        finally
        {
            try { Detach(); } catch { /* best effort */ }
            Interlocked.Exchange(ref _runAllInProgress, 0);
        }
    }

    // ==================== Scenario: Link Click ====================

    [RelayCommand]
    private Task RunLinkClickAsync()
        => RunScenarioSafeAsync(RunLinkClickCoreAsync);

    private async Task RunLinkClickCoreAsync()
    {
        if (!EnsureReady()) return;

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: link click");

        var completed = await TriggerNativeNavigationAndWaitAsync("document.getElementById('linkTarget').click();").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    // ==================== Scenario: 302 Redirect ====================

    [RelayCommand]
    private Task RunRedirect302Async()
        => RunScenarioSafeAsync(RunRedirect302CoreAsync);

    private async Task RunRedirect302CoreAsync()
    {
        if (!EnsureReady()) return;

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: 302 redirect");

        var completed = await TriggerNativeNavigationAndWaitAsync("document.getElementById('linkRedirect').click();").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");

        var starts = _nativeStarts.ToArray();
        if (starts.Length <= 1)
        {
            LogLine($"Native starts observed: {starts.Length} (WebView2 may collapse redirect steps).");
            return;
        }

        var corr = starts[0].CorrelationId;
        var allSame = starts.All(s => s.CorrelationId == corr);
        LogLine(allSame
            ? $"CorrelationId reused across {starts.Length} steps: {corr}"
            : "CorrelationId was NOT reused across redirect steps (unexpected).");
    }

    // ==================== Scenario: window.location ====================

    [RelayCommand]
    private Task RunWindowLocationAsync()
        => RunScenarioSafeAsync(RunWindowLocationCoreAsync);

    private async Task RunWindowLocationCoreAsync()
    {
        if (!EnsureReady()) return;

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: window.location");

        var completed = await TriggerNativeNavigationAndWaitAsync("window.location.href = '/target2';").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    // ==================== Scenario: Cancel ====================

    [RelayCommand]
    private Task RunCancelAsync()
        => RunScenarioSafeAsync(RunCancelCoreAsync);

    private async Task RunCancelCoreAsync()
    {
        if (!EnsureReady()) return;

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: cancel (deny in host)");

        var completed = await TriggerNativeNavigationAndWaitAsync("window.location.href = '/deny';").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    // ==================== Scenario: Script ====================

    [RelayCommand]
    private Task RunScriptAsync()
        => RunScenarioSafeAsync(RunScriptCoreAsync);

    private async Task RunScriptCoreAsync()
    {
        if (!EnsureReady()) return;

        LogLine("Scenario: InvokeScriptAsync");
        var result = await _adapter!.InvokeScriptAsync("1 + 1").ConfigureAwait(false);
        LogLine($"Script result: {result ?? "<null>"}");
    }

    // ==================== Scenario: WebMessage ====================

    [RelayCommand]
    private Task RunMessageAsync()
        => RunScenarioSafeAsync(RunMessageCoreAsync);

    private async Task RunMessageCoreAsync()
    {
        if (!EnsureReady()) return;

        var messageTcs = new TaskCompletionSource<WebMessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, WebMessageReceivedEventArgs e) => messageTcs.TrySetResult(e);

        _adapter!.WebMessageReceived += Handler;
        try
        {
            LogLine("Scenario: WebMessageReceived");
            await NavigateApiAsync(new Uri(_server!.BaseUri, "/message")).ConfigureAwait(false);
            var msg = await WaitAsync(messageTcs.Task, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            LogLine($"WebMessage: body='{msg.Body}', origin='{msg.Origin}', channelId={msg.ChannelId}, v={msg.ProtocolVersion}");
        }
        finally
        {
            _adapter!.WebMessageReceived -= Handler;
        }
    }

    // ==================== Scenario: Cookie CRUD ====================

    [RelayCommand]
    private Task RunCookieCrudAsync()
        => RunScenarioSafeAsync(RunCookieCrudCoreAsync);

    private async Task RunCookieCrudCoreAsync()
    {
        if (!EnsureReady()) return;

        LogLine("Scenario: Cookie CRUD");

        if (_adapter is not ICookieAdapter cookieAdapter)
        {
            LogLine("SKIP: adapter does not implement ICookieAdapter.");
            return;
        }

        // Navigate to the server first so cookies have a valid origin.
        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        // Set a test cookie.
        var testCookie = new WebViewCookie(
            "wv2_smoke_test", "hello_webview2", "127.0.0.1", "/",
            DateTimeOffset.UtcNow.AddHours(1), IsSecure: false, IsHttpOnly: false);
        await cookieAdapter.SetCookieAsync(testCookie).ConfigureAwait(false);
        LogLine("Cookie set: wv2_smoke_test=hello_webview2");

        // Get cookies and verify.
        var cookies = await cookieAdapter.GetCookiesAsync(_server!.BaseUri).ConfigureAwait(false);
        var found = cookies.FirstOrDefault(c =>
            string.Equals(c.Name, "wv2_smoke_test", StringComparison.Ordinal));

        if (found is not null)
        {
            LogLine($"Cookie get: {found.Name}={found.Value} domain={found.Domain} path={found.Path}");
        }
        else
        {
            LogLine("Cookie get: NOT FOUND (unexpected).");
        }

        // Delete the cookie.
        await cookieAdapter.DeleteCookieAsync(testCookie).ConfigureAwait(false);
        LogLine("Cookie deleted.");

        // Verify deletion.
        var afterDelete = await cookieAdapter.GetCookiesAsync(_server!.BaseUri).ConfigureAwait(false);
        var stillFound = afterDelete.Any(c =>
            string.Equals(c.Name, "wv2_smoke_test", StringComparison.Ordinal));
        LogLine(stillFound
            ? "Cookie still present after delete (unexpected)."
            : "Cookie confirmed deleted.");
    }

    // ==================== Scenario: Network Error ====================

    [RelayCommand]
    private Task RunErrorAsync()
        => RunScenarioSafeAsync(RunErrorCoreAsync);

    private async Task RunErrorCoreAsync()
    {
        if (!EnsureReady()) return;

        LogLine("Scenario: navigate to unreachable host");

        var navId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, NavigationCompletedEventArgs e)
        {
            if (e.NavigationId == navId) tcs.TrySetResult(e);
        }

        _adapter!.NavigationCompleted += Handler;
        try
        {
            // Port 1 on loopback: guaranteed connection refused (fast RST, no TCP SYN timeout).
            var unreachableUri = new Uri("http://127.0.0.1:1/");
            await _adapter.NavigateAsync(navId, unreachableUri).ConfigureAwait(false);
            var completed = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(15)).ConfigureAwait(false);

            LogLine($"Completed: status={completed.Status}, error={completed.Error?.GetType().Name ?? "null"}");

            // WebView2 may return various error types for unreachable hosts depending on OS/network:
            //   - WebViewNetworkException (CannotConnect, ConnectionReset, etc.)
            //   - WebViewTimeoutException (Timeout)
            //   - WebViewNavigationException (ConnectionRefused or other statuses)
            // Any non-null error with Failure status is acceptable.
            if (completed.Status == NavigationCompletedStatus.Failure && completed.Error is not null)
            {
                LogLine($"Navigation error confirmed: {completed.Error.GetType().Name}: {completed.Error.Message}");
            }
            else if (completed.Status == NavigationCompletedStatus.Failure)
            {
                LogLine("Navigation failed but no error object returned.");
            }
            else
            {
                LogLine($"Unexpected: expected Failure but got {completed.Status}");
            }
        }
        finally
        {
            _adapter!.NavigationCompleted -= Handler;
        }
    }

    // ==================== Scenario: Native Handle ====================

    [RelayCommand]
    private Task RunNativeHandleAsync()
        => RunScenarioSafeAsync(RunNativeHandleCoreAsync);

    private async Task RunNativeHandleCoreAsync()
    {
        if (!EnsureReady()) return;

        LogLine("Scenario: TryGetWebViewHandle()");

        // Ensure WebView2 is fully initialized.
        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        if (_adapter is not INativeWebViewHandleProvider handleProvider)
        {
            LogLine("SKIP: adapter does not implement INativeWebViewHandleProvider.");
            return;
        }

        var handle = handleProvider.TryGetWebViewHandle();
        if (handle is not null)
        {
            LogLine($"Handle: descriptor='{handle.HandleDescriptor}', ptr=0x{handle.Handle.ToString("x", CultureInfo.InvariantCulture)}");
            if (string.Equals(handle.HandleDescriptor, "WebView2", StringComparison.Ordinal))
            {
                LogLine("Handle descriptor 'WebView2' confirmed.");
            }
            else
            {
                LogLine($"Unexpected descriptor: '{handle.HandleDescriptor}' (expected 'WebView2').");
            }
        }
        else
        {
            LogLine("Handle is null (unexpected for attached adapter).");
        }
    }

    // ==================== Scenario: NavigateToStringAsync + baseUrl ====================

    [RelayCommand]
    private Task RunBaseUrlAsync()
        => RunScenarioSafeAsync(RunBaseUrlCoreAsync);

    private async Task RunBaseUrlCoreAsync()
    {
        if (!EnsureReady()) return;

        LogLine("Scenario: NavigateToStringAsync(html, baseUrl)");

        var html = "<html><body><h1 id='heading'>BaseUrl Smoke Test</h1></body></html>";
        var baseUrl = new Uri(_server!.BaseUri, "/base-url-test");

        var navId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, NavigationCompletedEventArgs e)
        {
            if (e.NavigationId == navId) tcs.TrySetResult(e);
        }

        _adapter!.NavigationCompleted += Handler;
        try
        {
            await _adapter.NavigateToStringAsync(navId, html, baseUrl).ConfigureAwait(false);
            var completed = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            LogLine($"BaseUrl navigation completed: {completed.Status}");

            if (completed.Status == NavigationCompletedStatus.Success)
            {
                // Verify the HTML content was loaded by reading the heading via script.
                var heading = await _adapter.InvokeScriptAsync("document.getElementById('heading').textContent").ConfigureAwait(false);
                LogLine($"Heading content: {heading ?? "<null>"}");
            }
        }
        finally
        {
            _adapter!.NavigationCompleted -= Handler;
        }
    }

    // ==================== Host interface ====================

    /// <summary>Sets the native host handle and optionally starts auto-run.</summary>
    public void SetHostHandle(IPlatformHandle handle)
    {
        _hostHandle = new AvaloniaNativeHandleAdapter(handle);
        LogLine($"Native host handle created: {handle.HandleDescriptor} 0x{handle.Handle.ToString("x", CultureInfo.InvariantCulture)}");

        if (AutoRun && Interlocked.Exchange(ref _autoRunStarted, 1) == 0)
        {
            _ = AutoRunMode == WebView2AutoRunMode.TeardownStress
                ? RunTeardownStressForAutoRunAsync()
                : RunAllForAutoRunAsync();
        }
    }

    /// <summary>Detaches the WebView adapter.</summary>
    public void Detach()
    {
        _adapter?.Detach();
        _adapter = null;
    }

    /// <inheritdoc />
    ValueTask<NativeNavigationStartingDecision> IWebViewAdapterHost.OnNativeNavigationStartingAsync(NativeNavigationStartingInfo info)
    {
        _nativeStarts.Enqueue(info);

        var navigationId = _navigationIdByCorrelation.GetOrAdd(info.CorrelationId, _ => Guid.NewGuid());
        _nextNativeNavigationIdTcs?.TrySetResult(navigationId);

        var deny = info.RequestUri.AbsolutePath.Equals("/deny", StringComparison.Ordinal);
        return ValueTask.FromResult(new NativeNavigationStartingDecision(IsAllowed: !deny, NavigationId: navigationId));
    }

    // ==================== Private helpers ====================

    private async Task RunScenarioSafeAsync(Func<Task> scenario)
    {
        try
        {
            await scenario().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
            LogLine(ex.ToString());
        }
    }

    private bool EnsureReady()
    {
        EnsureServerStarted();
        if (_adapter is null)
        {
            Status = "Not initialized. Click Initialize first.";
            return false;
        }

        return true;
    }

    private void EnsureServerStarted()
    {
        _server ??= new LoopbackHttpServer();
    }

    private Uri CreateIndexUri()
    {
        EnsureServerStarted();
        return new Uri(_server!.BaseUri, $"/index?t={Guid.NewGuid():N}");
    }

    private void ClearNativeStarts()
    {
        while (_nativeStarts.TryDequeue(out _))
        {
        }
    }

    private async Task NavigateApiAsync(Uri uri)
    {
        var navId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, NavigationCompletedEventArgs e)
        {
            if (e.NavigationId == navId)
            {
                tcs.TrySetResult(e);
            }
        }

        _adapter!.NavigationCompleted += Handler;
        try
        {
            await _adapter.NavigateAsync(navId, uri).ConfigureAwait(false);
            var completed = await WaitAsync(tcs.Task, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            LogLine($"API navigation completed: {completed.Status} {completed.RequestUri}");
        }
        finally
        {
            _adapter!.NavigationCompleted -= Handler;
        }
    }

    private async Task<NavigationCompletedEventArgs> TriggerNativeNavigationAndWaitAsync(string script)
    {
        var navIdTcs = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        _nextNativeNavigationIdTcs = navIdTcs;

        var completedTcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? s, NavigationCompletedEventArgs e)
        {
            if (navIdTcs.Task.IsCompletedSuccessfully && e.NavigationId == navIdTcs.Task.Result)
            {
                completedTcs.TrySetResult(e);
            }
        }

        _adapter!.NavigationCompleted += Handler;
        try
        {
            _ = await _adapter.InvokeScriptAsync(script).ConfigureAwait(false);
            var navId = await WaitAsync(navIdTcs.Task, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            LogLine($"Native navigation id issued: {navId}");
            return await WaitAsync(completedTcs.Task, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        }
        finally
        {
            _adapter!.NavigationCompleted -= Handler;
            _nextNativeNavigationIdTcs = null;
        }
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token)).ConfigureAwait(false);
        if (completed != task)
        {
            throw new TimeoutException($"Timed out after {timeout}.");
        }
        return await task.ConfigureAwait(false);
    }

    private void LogLine(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}";
        _logSink?.Invoke(line);

        if (AutoRun)
        {
            Console.WriteLine(line);
        }
    }

    private sealed class AvaloniaNativeHandleAdapter : INativeHandle
    {
        private readonly IPlatformHandle _inner;

        /// <summary>Wraps an Avalonia platform handle as INativeHandle.</summary>
        public AvaloniaNativeHandleAdapter(IPlatformHandle inner)
        {
            _inner = inner;
        }

        /// <inheritdoc />
        public nint Handle => _inner.Handle;
        /// <inheritdoc />
        public string HandleDescriptor => _inner.HandleDescriptor ?? string.Empty;
    }

    // ==================== Embedded loopback HTTP server ====================

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        /// <summary>Creates a loopback HTTP server on an ephemeral port.</summary>
        public LoopbackHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUri = new Uri($"http://127.0.0.1:{port}");
            _loop = Task.Run(AcceptLoopAsync);
        }

        /// <summary>Base URI of the loopback server.</summary>
        public Uri BaseUri { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient? client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var requestText = await ReadRequestAsync(stream).ConfigureAwait(false);
                var path = ParsePath(requestText);

                if (path == "/redirect")
                {
                    await WriteResponseAsync(
                        stream,
                        status: "302 Found",
                        contentType: "text/plain",
                        body: "redirect",
                        extraHeaders: $"Location: {BaseUri}/redirected\r\n").ConfigureAwait(false);
                    return;
                }

                var body = path switch
                {
                    "/index" => IndexHtml(),
                    "/target" => SimpleHtml("target"),
                    "/target2" => SimpleHtml("target2"),
                    "/redirected" => SimpleHtml("redirected"),
                    "/deny" => SimpleHtml("deny"),
                    "/message" => MessageHtml(),
                    _ => SimpleHtml("ok")
                };

                await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", body).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            return sb.ToString();
        }

        private static string ParsePath(string requestText)
        {
            var firstLineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? requestText[..firstLineEnd] : requestText;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return "/";
            }

            var rawPath = parts[1];
            var q = rawPath.IndexOf('?', StringComparison.Ordinal);
            return q >= 0 ? rawPath[..q] : rawPath;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            string status,
            string contentType,
            string body,
            string? extraHeaders = null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var headers = new StringBuilder();
            headers.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            headers.Append("Connection: close\r\n");
            headers.Append("Content-Type: ").Append(contentType).Append("\r\n");
            headers.Append("Content-Length: ").Append(bytes.Length).Append("\r\n");
            if (!string.IsNullOrEmpty(extraHeaders))
            {
                headers.Append(extraHeaders);
            }
            headers.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            await stream.WriteAsync(headerBytes).ConfigureAwait(false);
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        private static string SimpleHtml(string text)
            => $"<html><body><div id='content'>{WebUtility.HtmlEncode(text)}</div></body></html>";

        private static string IndexHtml()
        {
            return """
                   <html>
                     <body>
                       <a id="linkTarget" href="/target">target</a>
                       <a id="linkRedirect" href="/redirect">redirect</a>
                       <div id="status">ok</div>
                     </body>
                   </html>
                   """;
        }

        private static string MessageHtml()
        {
            // WebView2 bridge is injected via AddScriptToExecuteOnDocumentCreatedAsync,
            // making window.__agibuildWebView available before page scripts run.
            return """
                   <html>
                     <body>
                       <div id="status">message</div>
                       <script>
                         setTimeout(function() {
                           if (window.__agibuildWebView && window.__agibuildWebView.postMessage) {
                             window.__agibuildWebView.postMessage('hello');
                           } else if (window.chrome && window.chrome.webview) {
                             window.chrome.webview.postMessage('hello');
                           }
                         }, 100);
                       </script>
                     </body>
                   </html>
                   """;
        }
    }
}
