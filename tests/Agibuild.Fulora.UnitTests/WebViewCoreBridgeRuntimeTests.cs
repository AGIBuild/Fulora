using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreBridgeRuntimeTests
{
    private static (WebViewCoreBridgeRuntime Runtime, MockWebViewAdapter Adapter, WebViewCoreContext Context, List<string> InvokedScripts)
        CreateRuntime(bool enableDevToolsByDefault = false, WebViewLifecycleStateMachine? lifecycle = null)
    {
        var adapter = MockWebViewAdapter.Create();
        var invokedScripts = new List<string>();
        adapter.ScriptCallback = script =>
        {
            invokedScripts.Add(script);
            return null;
        };
        var context = WebViewCoreTestContext.Create(adapter, lifecycle: lifecycle);
        var runtime = new WebViewCoreBridgeRuntime(context, enableDevToolsByDefault);
        return (runtime, adapter, context, invokedScripts);
    }

    [Fact]
    public async Task Bridge_property_auto_enables_bridge_and_records_stub_injection()
    {
        var (runtime, _, _, invokedScripts) = CreateRuntime(enableDevToolsByDefault: true);

        var bridge = runtime.Bridge;

        Assert.NotNull(bridge);
        Assert.NotNull(runtime.Rpc);
        Assert.True(runtime.IsBridgeEnabled);

        // The JS stub is injected via a fire-and-forget background task. Give the async
        // operation queue a moment to process the injection before asserting.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Contains(invokedScripts, script => script.Contains("window.agWebView", StringComparison.Ordinal));

        runtime.Dispose();
    }

    [Fact]
    public void HandleWebMessage_when_allowed_and_non_rpc_forwards_to_host()
    {
        var (runtime, _, context, _) = CreateRuntime();
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        WebMessageReceivedEventArgs? forwarded = null;
        context.Events.WebMessageReceived += (_, args) => forwarded = args;

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", context.ChannelId);
        runtime.HandleAdapterWebMessageReceivedOnUiThread(message);

        Assert.Same(message, forwarded);

        runtime.Dispose();
    }

    [Fact]
    public void HandleWebMessage_when_denied_emits_drop_diagnostics()
    {
        var (runtime, _, context, _) = CreateRuntime();
        var dropSink = new RecordingDropSink();
        var diagnosticsSink = new MemoryFuloraDiagnosticsSink();
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
                context.ChannelId));

        var drop = Assert.Single(dropSink.Diagnostics);
        Assert.Equal(WebMessageDropReason.OriginNotAllowed, drop.Reason);

        var diagnosticEvent = Assert.Single(diagnosticsSink.Events);
        Assert.Equal("runtime.webmessage.dropped", diagnosticEvent.EventName);
        Assert.Equal(context.ChannelId.ToString("D"), diagnosticEvent.ChannelId);
        Assert.Equal("https://blocked.example", diagnosticEvent.Attributes["origin"]);

        runtime.Dispose();
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_when_lifecycle_disposed_does_not_dispatch()
    {
        // B1 regression guard: if the lifecycle transitions to disposed before an adapter event
        // arrives, the UI-thread path must never be invoked (otherwise bridge state may be touched
        // after teardown). Bridge must be enabled first because EnableWebMessageBridge itself guards
        // with ThrowIfDisposed.
        var lifecycle = WebViewCoreTestContext.CreateReadyLifecycle();
        var (runtime, _, context, _) = CreateRuntime(lifecycle: lifecycle);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        WebMessageReceivedEventArgs? forwarded = null;
        context.Events.WebMessageReceived += (_, args) => forwarded = args;

        lifecycle.TryTransitionToDisposed();

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", context.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Null(forwarded);
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_when_adapter_destroyed_does_not_dispatch()
    {
        // B1 regression guard: mirrors disposed path for the adapter-destroyed signal so both
        // short-circuit conditions documented in UiThreadHelper.SafeDispatch stay exercised.
        var lifecycle = WebViewCoreTestContext.CreateReadyLifecycle();
        var (runtime, _, context, _) = CreateRuntime(lifecycle: lifecycle);
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        WebMessageReceivedEventArgs? forwarded = null;
        context.Events.WebMessageReceived += (_, args) => forwarded = args;

        lifecycle.MarkAdapterDestroyedOnce(() => { });

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", context.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Null(forwarded);

        runtime.Dispose();
    }

    [Fact]
    public void HandleAdapterWebMessageReceived_on_ui_thread_forwards_synchronously()
    {
        // B1 regression guard: when dispatcher.CheckAccess() returns true, SafeDispatch must invoke
        // the UI-thread callback inline rather than queueing it, so the message is forwarded
        // synchronously (matching the previous WebViewCore behaviour).
        var (runtime, _, context, _) = CreateRuntime();
        runtime.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });

        WebMessageReceivedEventArgs? forwarded = null;
        context.Events.WebMessageReceived += (_, args) => forwarded = args;

        var message = new WebMessageReceivedEventArgs("""{"type":"custom"}""", "*", context.ChannelId);
        runtime.HandleAdapterWebMessageReceived(message);

        Assert.Same(message, forwarded);

        runtime.Dispose();
    }

    private sealed class RecordingDropSink : IWebMessageDropDiagnosticsSink
    {
        public List<WebMessageDropDiagnostic> Diagnostics { get; } = [];

        public void OnMessageDropped(in WebMessageDropDiagnostic diagnostic)
            => Diagnostics.Add(diagnostic);
    }
}
