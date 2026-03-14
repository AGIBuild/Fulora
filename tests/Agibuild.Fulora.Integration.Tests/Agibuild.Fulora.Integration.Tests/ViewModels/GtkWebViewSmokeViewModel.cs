using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;

namespace Agibuild.Fulora.Integration.Tests.ViewModels;

/// <summary>
/// Linux / WebKitGTK smoke validation.
/// Focuses on minimal, release-critical end-to-end flows without timing sleeps:
/// - navigation start/complete
/// - cancel via NavigationStarted.Cancel
/// - script invocation
/// - WebMessage receive path (bridge enabled)
/// - DevTools open/close (GTK supports inspector show/close)
/// </summary>
public sealed partial class GtkWebViewSmokeViewModel : ViewModelBase
{
    private readonly Action<string>? _logSink;
    private LoopbackHttpServer? _server;
    private int _autoRunStarted;
    private int _runAllInProgress;

    public GtkWebViewSmokeViewModel(Action<string>? logSink = null)
    {
        _logSink = logSink;
        Status = "Not started.";
    }

    public WebView? WebViewControl { get; set; }

    public bool AutoRun { get; set; }

    public event Action<int>? AutoRunCompleted;

    [ObservableProperty]
    private string _status = string.Empty;

    public void OnWebViewLoaded()
    {
        EnsureServerStarted();

        if (AutoRun && Interlocked.Exchange(ref _autoRunStarted, 1) == 0)
        {
            _ = RunAllForAutoRunAsync();
        }
    }

    [RelayCommand]
    private Task InitializeAsync()
    {
        try
        {
            EnsureServerStarted();

            if (!OperatingSystem.IsLinux())
            {
                Status = "SKIP: GTK smoke requires Linux.";
                return Task.CompletedTask;
            }

            if (WebViewControl is null)
            {
                Status = "Waiting for WebViewControl...";
                return Task.CompletedTask;
            }

            if (!WebViewControl.IsAvailable)
            {
                Status = "FAIL: WebView adapter is not available on this host.";
                LogLine("WebView.IsAvailable == false (adapter not registered or failed to attach).");
                return Task.CompletedTask;
            }

            Status = "Ready.";
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
        _ = await RunAllCoreAsync();
    }

    private async Task RunAllForAutoRunAsync()
    {
        var ok = await RunAllCoreAsync();
        LogLine(ok ? "GTK smoke: PASS" : "GTK smoke: FAIL");
        AutoRunCompleted?.Invoke(ok ? 0 : 1);
    }

    private async Task<bool> RunAllCoreAsync()
    {
        if (Interlocked.Exchange(ref _runAllInProgress, 1) != 0)
        {
            LogLine("RunAll ignored: already in progress.");
            return false;
        }

        try
        {
            Status = "Running...";

            await InitializeAsync().ConfigureAwait(true);
            if (WebViewControl is null || !WebViewControl.IsAvailable || _server is null)
            {
                Status = "Failed: not initialized.";
                return false;
            }

            var ok = true;
            ok &= await Scenario_Navigation_Succeeds().ConfigureAwait(true);
            ok &= await Scenario_CancelNavigation_CompletesCanceled().ConfigureAwait(true);
            ok &= await Scenario_InvokeScript_ReturnsExpectedValue().ConfigureAwait(true);
            ok &= await Scenario_WebMessage_ReceivesHello().ConfigureAwait(true);
            ok &= await Scenario_DevTools_TogglesOpenClose().ConfigureAwait(true);

            Status = ok ? "PASS" : "FAIL";
            return ok;
        }
        catch (Exception ex)
        {
            Status = $"FAIL: {ex.Message}";
            LogLine(ex.ToString());
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _runAllInProgress, 0);
        }
    }

    // ---------------------------------------------------------------------------
    //  Scenarios
    // ---------------------------------------------------------------------------

    private async Task<bool> Scenario_Navigation_Succeeds()
    {
        try
        {
            LogLine("Scenario: Navigation start/complete");

            var wv = WebViewControl!;
            var completedTcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? s, NavigationCompletedEventArgs e) => completedTcs.TrySetResult(e);

            wv.NavigationCompleted += Completed;
            try
            {
                var uri = new Uri(_server!.BaseUri, "/index");
                await Dispatcher.UIThread.InvokeAsync(() => wv.NavigateAsync(uri));
                var completed = await WaitAsync(completedTcs.Task, TimeSpan.FromSeconds(15));

                LogLine($"  Completed: status={completed.Status}, uri={completed.RequestUri}");
                if (completed.Status != NavigationCompletedStatus.Success)
                    return Fail($"Expected Success, got {completed.Status}");

                return Pass();
            }
            finally
            {
                wv.NavigationCompleted -= Completed;
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<bool> Scenario_CancelNavigation_CompletesCanceled()
    {
        try
        {
            LogLine("Scenario: Cancel via NavigationStarted.Cancel");

            var wv = WebViewControl!;
            var completedTcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Started(object? s, NavigationStartingEventArgs e)
            {
                if (e.RequestUri.AbsolutePath.Equals("/deny", StringComparison.Ordinal))
                {
                    e.Cancel = true;
                }
            }

            void Completed(object? s, NavigationCompletedEventArgs e) => completedTcs.TrySetResult(e);

            wv.NavigationStarted += Started;
            wv.NavigationCompleted += Completed;
            try
            {
                var uri = new Uri(_server!.BaseUri, "/deny");
                await Dispatcher.UIThread.InvokeAsync(() => wv.NavigateAsync(uri));

                var completed = await WaitAsync(completedTcs.Task, TimeSpan.FromSeconds(15));
                LogLine($"  Completed: status={completed.Status}, uri={completed.RequestUri}");

                if (completed.Status != NavigationCompletedStatus.Canceled)
                    return Fail($"Expected Canceled, got {completed.Status}");

                return Pass();
            }
            finally
            {
                wv.NavigationStarted -= Started;
                wv.NavigationCompleted -= Completed;
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<bool> Scenario_InvokeScript_ReturnsExpectedValue()
    {
        try
        {
            LogLine("Scenario: InvokeScriptAsync");

            var wv = WebViewControl!;
            var completedTcs = new TaskCompletionSource<NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? s, NavigationCompletedEventArgs e) => completedTcs.TrySetResult(e);

            wv.NavigationCompleted += Completed;
            try
            {
                var uri = new Uri(_server!.BaseUri, "/script");
                await Dispatcher.UIThread.InvokeAsync(() => wv.NavigateAsync(uri));
                var completed = await WaitAsync(completedTcs.Task, TimeSpan.FromSeconds(15));
                if (completed.Status != NavigationCompletedStatus.Success)
                    return Fail($"Expected Success, got {completed.Status}");

                var value = await wv.InvokeScriptAsync("document.getElementById('value')?.textContent");
                LogLine($"  value='{value}'");

                if (!string.Equals(value, "42", StringComparison.Ordinal))
                    return Fail($"Expected '42', got '{value ?? "<null>"}'");

                return Pass();
            }
            finally
            {
                wv.NavigationCompleted -= Completed;
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<bool> Scenario_WebMessage_ReceivesHello()
    {
        try
        {
            LogLine("Scenario: WebMessageReceived (bridge enabled)");

            var wv = WebViewControl!;

            var origin = _server!.BaseUri.GetLeftPart(UriPartial.Authority);
            await Dispatcher.UIThread.InvokeAsync(() =>
                wv.EnableWebMessageBridge(new WebMessageBridgeOptions
                {
                    AllowedOrigins = new HashSet<string>(StringComparer.Ordinal) { origin },
                    ProtocolVersion = 1
                }));

            var msgTcs = new TaskCompletionSource<WebMessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Handler(object? s, WebMessageReceivedEventArgs e) => msgTcs.TrySetResult(e);

            wv.WebMessageReceived += Handler;
            try
            {
                var uri = new Uri(_server.BaseUri, "/message");
                await Dispatcher.UIThread.InvokeAsync(() => wv.NavigateAsync(uri));

                var msg = await WaitAsync(msgTcs.Task, TimeSpan.FromSeconds(15));
                LogLine($"  body='{msg.Body}', origin='{msg.Origin}', channelId={msg.ChannelId}, v={msg.ProtocolVersion}");

                if (!string.Equals(msg.Body, "hello", StringComparison.Ordinal))
                    return Fail($"Expected 'hello', got '{msg.Body}'");

                return Pass();
            }
            finally
            {
                wv.WebMessageReceived -= Handler;
            }
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private async Task<bool> Scenario_DevTools_TogglesOpenClose()
    {
        try
        {
            LogLine("Scenario: DevTools open/close (GTK)");

            var wv = WebViewControl!;

            var initial = await wv.IsDevToolsOpenAsync();
            LogLine($"  IsDevToolsOpen (initial): {initial}");

            await wv.OpenDevToolsAsync();
            var afterOpen = await wv.IsDevToolsOpenAsync();
            LogLine($"  IsDevToolsOpen (after open): {afterOpen}");

            await wv.CloseDevToolsAsync();
            var afterClose = await wv.IsDevToolsOpenAsync();
            LogLine($"  IsDevToolsOpen (after close): {afterClose}");

            if (!afterOpen)
                return Fail("Expected DevTools to be open after OpenDevToolsAsync().");
            if (afterClose)
                return Fail("Expected DevTools to be closed after CloseDevToolsAsync().");

            return Pass();
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    // ---------------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------------

    private void EnsureServerStarted()
        => _server ??= new LoopbackHttpServer();

    private bool Pass()
    {
        LogLine("  PASS");
        return true;
    }

    private bool Fail(string message)
    {
        LogLine($"  FAIL: {message}");
        return false;
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

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:F0}s.");
        return await task;
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        public LoopbackHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseUri = new Uri($"http://127.0.0.1:{port}");
            _loop = Task.Run(AcceptLoopAsync);
        }

        public Uri BaseUri { get; }

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
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
                }
                catch
                {
                    client?.Dispose();
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var requestText = await ReadRequestAsync(stream);
                var path = ParsePath(requestText);

                var body = path switch
                {
                    "/index" => IndexHtml(),
                    "/deny" => SimpleHtml("deny"),
                    "/script" => ScriptHtml(),
                    "/message" => MessageHtml(),
                    _ => SimpleHtml("ok")
                };

                await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", body);
            }
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read <= 0)
                    break;

                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }

            return sb.ToString();
        }

        private static string ParsePath(string requestText)
        {
            var firstLineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
            var firstLine = firstLineEnd >= 0 ? requestText[..firstLineEnd] : requestText;
            var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return "/";

            var rawPath = parts[1];
            var q = rawPath.IndexOf('?', StringComparison.Ordinal);
            return q >= 0 ? rawPath[..q] : rawPath;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            string status,
            string contentType,
            string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var headers = new StringBuilder();
            headers.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            headers.Append("Connection: close\r\n");
            headers.Append("Content-Type: ").Append(contentType).Append("\r\n");
            headers.Append("Content-Length: ").Append(bytes.Length).Append("\r\n");
            headers.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            await stream.WriteAsync(headerBytes);
            await stream.WriteAsync(bytes);
        }

        private static string SimpleHtml(string text)
            => $"<html><body><div id='content'>{WebUtility.HtmlEncode(text)}</div></body></html>";

        private static string IndexHtml()
        {
            return """
                   <html>
                     <body>
                       <div id="status">ok</div>
                     </body>
                   </html>
                   """;
        }

        private static string ScriptHtml()
        {
            return """
                   <html>
                     <body>
                       <div id="value">42</div>
                     </body>
                   </html>
                   """;
        }

        private static string MessageHtml()
        {
            // Prefer the injected helper when present; fall back to platform APIs.
            return """
                   <html>
                     <body>
                       <div id="status">message</div>
                       <script>
                         (function() {
                           var msg = 'hello';
                           if (window.__agibuildWebView && window.__agibuildWebView.postMessage) {
                             window.__agibuildWebView.postMessage(msg);
                             return;
                           }
                           if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.agibuildWebView) {
                             window.webkit.messageHandlers.agibuildWebView.postMessage(msg);
                             return;
                           }
                           if (window.chrome && window.chrome.webview) {
                             window.chrome.webview.postMessage(msg);
                             return;
                           }
                         })();
                       </script>
                     </body>
                   </html>
                   """;
        }
    }
}

