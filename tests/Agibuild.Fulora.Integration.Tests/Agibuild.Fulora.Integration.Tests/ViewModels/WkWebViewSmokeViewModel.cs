using System.Collections.Concurrent;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPlatformHandle = global::Avalonia.Platform.IPlatformHandle;

namespace Agibuild.Fulora.Integration.Tests.ViewModels;

/// <summary>ViewModel for WKWebView smoke tests.</summary>
public partial class WkWebViewSmokeViewModel : ViewModelBase, IWebViewAdapterHost
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
    public WkWebViewSmokeViewModel(Action<string>? logSink = null)
    {
        _logSink = logSink;
        ChannelId = Guid.NewGuid();
        Status = "Not started.";
    }

    /// <summary>Channel identifier for WebView messaging.</summary>
    public Guid ChannelId { get; }

    /// <summary>Whether to auto-run tests when host handle is set.</summary>
    public bool AutoRun { get; set; }

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

                LogLine("WebView adapter attached.");
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
        LogLine(ok ? "WK smoke: PASS" : "WK smoke: FAIL");
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

            await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

            await RunLinkClickCoreAsync().ConfigureAwait(false);
            await RunRedirect302CoreAsync().ConfigureAwait(false);
            await RunWindowLocationCoreAsync().ConfigureAwait(false);
            await RunCancelCoreAsync().ConfigureAwait(false);
            await RunScriptAsync().ConfigureAwait(false);
            await RunMessageAsync().ConfigureAwait(false);

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

    [RelayCommand]
    private Task RunLinkClickAsync()
        => RunScenarioSafeAsync(RunLinkClickCoreAsync);

    private async Task RunLinkClickCoreAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: link click");

        var completed = await TriggerNativeNavigationAndWaitAsync("document.getElementById('linkTarget').click();").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    [RelayCommand]
    private Task RunRedirect302Async()
        => RunScenarioSafeAsync(RunRedirect302CoreAsync);

    private async Task RunRedirect302CoreAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: 302 redirect");

        var completed = await TriggerNativeNavigationAndWaitAsync("document.getElementById('linkRedirect').click();").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");

        var starts = _nativeStarts.ToArray();
        if (starts.Length <= 1)
        {
            LogLine($"Native starts observed: {starts.Length} (WK may not surface redirect steps as separate policy callbacks in this environment).");
            return;
        }

        var corr = starts[0].CorrelationId;
        var allSame = starts.All(s => s.CorrelationId == corr);
        LogLine(allSame
            ? $"CorrelationId reused across {starts.Length} steps: {corr}"
            : "CorrelationId was NOT reused across redirect steps (unexpected).");
    }

    [RelayCommand]
    private Task RunWindowLocationAsync()
        => RunScenarioSafeAsync(RunWindowLocationCoreAsync);

    private async Task RunWindowLocationCoreAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: window.location");

        var completed = await TriggerNativeNavigationAndWaitAsync("window.location.href = '/target2';").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    [RelayCommand]
    private Task RunCancelAsync()
        => RunScenarioSafeAsync(RunCancelCoreAsync);

    private async Task RunCancelCoreAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

        await NavigateApiAsync(CreateIndexUri()).ConfigureAwait(false);

        ClearNativeStarts();
        LogLine("Scenario: cancel (deny in host)");

        var completed = await TriggerNativeNavigationAndWaitAsync("window.location.href = '/deny';").ConfigureAwait(false);
        LogLine($"Completed: {completed.Status} {completed.RequestUri}");
    }

    [RelayCommand]
    private async Task RunScriptAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

        LogLine("Scenario: InvokeScriptAsync");
        var result = await _adapter!.InvokeScriptAsync("1 + 1").ConfigureAwait(false);
        LogLine($"Script result: {result ?? "<null>"}");
    }

    [RelayCommand]
    private async Task RunMessageAsync()
    {
        if (!EnsureReady())
        {
            return;
        }

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

    /// <summary>Sets the native host handle and optionally starts auto-run.</summary>
    public void SetHostHandle(IPlatformHandle handle)
    {
        _hostHandle = new AvaloniaNativeHandleAdapter(handle);
        LogLine($"Native host handle created: {handle.HandleDescriptor} 0x{handle.Handle.ToString("x", CultureInfo.InvariantCulture)}");

        if (AutoRun && Interlocked.Exchange(ref _autoRunStarted, 1) == 0)
        {
            _ = RunAllForAutoRunAsync();
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
            return """
                   <html>
                     <body>
                       <div id="status">message</div>
                       <script>
                         setTimeout(function() {
                           if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.agibuildWebView) {
                             window.webkit.messageHandlers.agibuildWebView.postMessage('hello');
                           }
                         }, 0);
                       </script>
                     </body>
                   </html>
                   """;
        }
    }
}

