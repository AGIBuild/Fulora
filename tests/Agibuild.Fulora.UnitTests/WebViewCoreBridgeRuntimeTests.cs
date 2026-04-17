using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreBridgeRuntimeTests
{
    [Fact]
    public void Bridge_property_auto_enables_bridge_and_records_stub_injection()
    {
        var host = new TestBridgeHost();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: true);

        var bridge = runtime.Bridge;

        Assert.NotNull(bridge);
        Assert.NotNull(runtime.Rpc);
        Assert.True(runtime.IsBridgeEnabled);
        Assert.Contains("EnableWebMessageBridge.JsStub", host.ObservedBackgroundOperations);
        Assert.Contains(host.InvokedScripts, script => script.Contains("window.agWebView", StringComparison.Ordinal));
    }

    [Fact]
    public void HandleWebMessage_when_allowed_and_non_rpc_forwards_to_host()
    {
        var host = new TestBridgeHost();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: false);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", host.ChannelId);
        runtime.HandleAdapterWebMessageReceivedOnUiThread(message);

        Assert.Same(message, host.LastForwardedMessage);
    }

    [Fact]
    public void HandleWebMessage_when_denied_emits_drop_diagnostics()
    {
        var host = new TestBridgeHost();
        var dropSink = new RecordingDropSink();
        var diagnosticsSink = new MemoryFuloraDiagnosticsSink();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: false);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "https://allowed.example" },
            DropDiagnosticsSink = dropSink,
            DiagnosticsSink = diagnosticsSink
        });

        runtime.HandleAdapterWebMessageReceivedOnUiThread(
            new WebMessageReceivedEventArgs(
                """{"type":"blocked"}""",
                "https://blocked.example",
                host.ChannelId));

        var drop = Assert.Single(dropSink.Diagnostics);
        Assert.Equal(WebMessageDropReason.OriginNotAllowed, drop.Reason);

        var diagnosticEvent = Assert.Single(diagnosticsSink.Events);
        Assert.Equal("runtime.webmessage.dropped", diagnosticEvent.EventName);
        Assert.Equal(host.ChannelId.ToString("D"), diagnosticEvent.ChannelId);
        Assert.Equal("https://blocked.example", diagnosticEvent.Attributes["origin"]);
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_when_host_disposed_does_not_dispatch()
    {
        // B1 regression guard: the dispatch-symmetry refactor moved SafeDispatch filtering from
        // WebViewCore into WebViewCoreBridgeRuntime. If the host is disposed before the adapter
        // event arrives, the UI-thread path must never be invoked (otherwise bridge state may be
        // touched after teardown). Bridge must be enabled first because EnableWebMessageBridge
        // itself guards with ThrowIfDisposed.
        var host = new TestBridgeHost();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: false);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        host.IsDisposed = true;

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", host.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Null(host.LastForwardedMessage);
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_when_adapter_destroyed_does_not_dispatch()
    {
        // B1 regression guard: mirrors disposed path for the adapter-destroyed signal so both
        // short-circuit conditions documented in UiThreadHelper.SafeDispatch stay exercised.
        var host = new TestBridgeHost();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: false);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        host.IsAdapterDestroyed = true;

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", host.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Null(host.LastForwardedMessage);
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_on_ui_thread_forwards_synchronously()
    {
        // B1 regression guard: when dispatcher.CheckAccess() returns true, SafeDispatch must invoke
        // the UI-thread callback inline rather than queueing it, so the message is forwarded
        // synchronously (matching the previous WebViewCore behaviour).
        var host = new TestBridgeHost();
        using var runtime = new WebViewCoreBridgeRuntime(
            host,
            new TestDispatcher(),
            NullLogger.Instance,
            enableDevToolsByDefault: false);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", host.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Same(message, host.LastForwardedMessage);
    }

    private sealed class TestBridgeHost : IWebViewCoreBridgeHost
    {
        private readonly int _uiThreadId = Environment.CurrentManagedThreadId;

        public Guid ChannelId { get; } = Guid.NewGuid();

        public bool IsDisposed { get; set; }

        public bool IsAdapterDestroyed { get; set; }

        public List<string> ObservedBackgroundOperations { get; } = [];

        public List<string> InvokedScripts { get; } = [];

        public WebMessageReceivedEventArgs? LastForwardedMessage { get; private set; }

        public Task<string?> InvokeScriptAsync(string script)
        {
            InvokedScripts.Add(script);
            return Task.FromResult<string?>(null);
        }

        public void ObserveBackgroundTask(Task task, string operationType)
            => ObservedBackgroundOperations.Add(operationType);

        public void RaiseWebMessageReceived(WebMessageReceivedEventArgs args)
            => LastForwardedMessage = args;

        public void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(IsDisposed, nameof(TestBridgeHost));

        public void ThrowIfNotOnUiThread(string apiName)
        {
            if (Environment.CurrentManagedThreadId != _uiThreadId)
            {
                throw new InvalidOperationException($"'{apiName}' must be called on the UI thread.");
            }
        }
    }

    private sealed class RecordingDropSink : IWebMessageDropDiagnosticsSink
    {
        public List<WebMessageDropDiagnostic> Diagnostics { get; } = [];

        public void OnMessageDropped(in WebMessageDropDiagnostic diagnostic)
            => Diagnostics.Add(diagnostic);
    }
}
