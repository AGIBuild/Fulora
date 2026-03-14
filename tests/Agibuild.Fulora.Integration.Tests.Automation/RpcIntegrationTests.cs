using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the JS ↔ C# RPC feature (IWebViewRpcService).
/// Exercises the full WebViewCore → WebViewRpcService → MockAdapter stack.
///
/// HOW IT WORKS (for newcomers):
///
///   The RPC system uses JSON-RPC 2.0 over the WebMessage bridge.
///
///   C# → JS call flow:
///     1. C# calls Rpc.InvokeAsync("method", args)
///     2. WebViewRpcService serializes a JSON-RPC request and sends it via adapter.InvokeScriptAsync
///     3. JS processes it and sends back a response via window.chrome.webview.postMessage
///     4. WebViewCore receives it, passes to WebViewRpcService.TryProcessMessage
///     5. The pending Task resolves with the result
///
///   JS → C# call flow:
///     1. JS calls window.agWebView.rpc.invoke("method", params)
///     2. JS sends a JSON-RPC request via postMessage
///     3. WebViewCore receives it, dispatches to WebViewRpcService
///     4. WebViewRpcService calls the registered C# handler
///     5. Response is sent back to JS via InvokeScriptAsync
///
///   In these tests, we simulate JS messages using adapter.RaiseWebMessage()
///   and capture outgoing scripts via adapter.ScriptResult / captured scripts.
/// </summary>
public sealed class RpcIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    /// <summary>Helper: creates a WebViewCore with bridge enabled and returns the RPC service.</summary>
    private (WebViewCore Core, MockWebViewAdapter Adapter) CreateCoreWithRpc()
    {
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        return (core, adapter);
    }

    // ──────────────────── Test 1: Rpc is null before bridge ────────────────────

    [AvaloniaFact]
    public void Rpc_is_null_before_bridge_enabled()
    {
        // Arrange: create core WITHOUT enabling the bridge
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);

        // Assert: Rpc property is null
        Assert.Null(core.Rpc);
    }

    // ──────────────────── Test 2: Rpc available after bridge ────────────────────

    [AvaloniaFact]
    public void Rpc_available_after_bridge_enabled()
    {
        // Arrange & Act
        var (core, _) = CreateCoreWithRpc();

        // Assert
        Assert.NotNull(core.Rpc);
        core.Dispose();
    }

    // ──────────────────── Test 3: C# handler responds to JS request ────────────────────

    [AvaloniaFact]
    public void CSharp_handler_responds_to_JS_request()
    {
        // Arrange
        var (core, adapter) = CreateCoreWithRpc();

        // Register a C# handler: "add" takes {a, b} and returns a + b
        core.Rpc!.Handle("add", args =>
        {
            var a = args!.Value.GetProperty("a").GetInt32();
            var b = args!.Value.GetProperty("b").GetInt32();
            return Task.FromResult<object?>(a + b);
        });

        // Capture scripts sent back to JS (the response)
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        // Act: simulate JS sending a JSON-RPC request
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"test-1","method":"add","params":{"a":3,"b":7}}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();

        DispatcherTestPump.WaitUntil(_dispatcher, () => capturedScripts.Any(s => s.Contains("test-1")));

        // Assert: response script was sent back containing the result (10)
        Assert.Contains(capturedScripts, s => s.Contains("test-1") && s.Contains("10"));
        core.Dispose();
    }

    // ──────────────────── Test 4: C# invokes JS method ────────────────────

    [AvaloniaFact]
    public async Task CSharp_invokes_JS_method_and_gets_result()
    {
        // Arrange
        var (core, adapter) = CreateCoreWithRpc();
        var rpc = core.Rpc!;

        // Start the C#→JS call (it will wait for a response)
        var callTask = rpc.InvokeAsync<string>("getTitle");

        // Pump deterministically until request script is emitted.
        DispatcherTestPump.WaitUntil(_dispatcher, () => adapter.LastScript is not null);

        // Extract the RPC request ID from the script the adapter received
        var callId = ExtractRpcId(adapter.LastScript!);

        // Act: simulate JS responding with a result
        adapter.RaiseWebMessage(
            "{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":\"My Page Title\"}",
            "*", core.ChannelId);
        DispatcherTestPump.WaitUntil(_dispatcher, () => callTask.IsCompleted);

        // Assert
        var title = await callTask;
        Assert.Equal("My Page Title", title);
        core.Dispose();
    }

    // ──────────────────── Test 5: Unknown method returns error ────────────────────

    [AvaloniaFact]
    public void Unknown_method_returns_method_not_found_error()
    {
        // Arrange
        var (core, adapter) = CreateCoreWithRpc();
        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        // Act: JS calls a method that no C# handler is registered for
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"err-1","method":"nonExistent","params":null}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        DispatcherTestPump.WaitUntil(_dispatcher, () => capturedScripts.Any(s => s.Contains("err-1")));

        // Assert: error response with code -32601 (Method not found)
        Assert.Contains(capturedScripts, s => s.Contains("err-1") && s.Contains("-32601"));
        core.Dispose();
    }

    // ──────────────────── Test 6: Handler exception returns error ────────────────────

    [AvaloniaFact]
    public void Handler_exception_returns_internal_error()
    {
        // Arrange
        var (core, adapter) = CreateCoreWithRpc();
        core.Rpc!.Handle("crash", _ => throw new InvalidOperationException("Boom!"));

        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        // Act: JS calls the crashing handler
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"err-2","method":"crash","params":null}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        DispatcherTestPump.WaitUntil(_dispatcher, () => capturedScripts.Any(s => s.Contains("err-2")));

        // Assert: error response with code -32603 (Internal error) and our message
        Assert.Contains(capturedScripts, s =>
            s.Contains("err-2") && s.Contains("-32603") && s.Contains("Boom!"));
        core.Dispose();
    }

    // ──────────────────── Test 7: UnregisterHandler works ────────────────────

    [AvaloniaFact]
    public void UnregisterHandler_causes_method_not_found()
    {
        // Arrange
        var (core, adapter) = CreateCoreWithRpc();
        core.Rpc!.Handle("temp", _ => Task.FromResult<object?>("ok"));

        // Remove the handler
        core.Rpc!.UnregisterHandler("temp");

        var capturedScripts = new List<string>();
        adapter.ScriptCallback = script => { capturedScripts.Add(script); return null; };

        // Act: JS calls the removed method
        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"rm-1","method":"temp","params":null}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
        DispatcherTestPump.WaitUntil(_dispatcher, () => capturedScripts.Any(s => s.Contains("rm-1")));

        // Assert: method not found error
        Assert.Contains(capturedScripts, s => s.Contains("rm-1") && s.Contains("-32601"));
        core.Dispose();
    }

    // ──────────────────── Helpers ────────────────────

    /// <summary>
    /// Extracts the JSON-RPC "id" from a script like:
    ///   window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch("{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"getTitle\"}")
    /// </summary>
    private static string ExtractRpcId(string script)
    {
        var start = script.IndexOf("_dispatch(") + "_dispatch(".Length;
        var end = script.LastIndexOf(')');
        var jsonString = script[start..end];
        // The outer value is a JSON string literal; deserialize to get the inner JSON
        var innerJson = JsonSerializer.Deserialize<string>(jsonString)!;
        using var doc = JsonDocument.Parse(innerJson);
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}
