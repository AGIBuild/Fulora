using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class CoverageGapTests
{
    [Fact]
    public void WebMessageEnvelope_value_equality()
    {
        var channelId = Guid.NewGuid();
        var a = new WebMessageEnvelope("body", "origin", channelId, 1);
        var b = new WebMessageEnvelope("body", "origin", channelId, 1);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void WebMessageEnvelope_value_inequality()
    {
        var channelId = Guid.NewGuid();
        var a = new WebMessageEnvelope("body1", "origin", channelId, 1);
        var b = new WebMessageEnvelope("body2", "origin", channelId, 1);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void WebMessageEnvelope_ToString_contains_fields()
    {
        var channelId = Guid.NewGuid();
        var envelope = new WebMessageEnvelope("hello", "https://origin.test", channelId, 1);

        var str = envelope.ToString();
        Assert.Contains("hello", str);
        Assert.Contains("https://origin.test", str);
    }

    [Fact]
    public void WebMessageEnvelope_deconstruction()
    {
        var channelId = Guid.NewGuid();
        var envelope = new WebMessageEnvelope("body", "origin", channelId, 2);

        var (body, origin, channel, version) = envelope;
        Assert.Equal("body", body);
        Assert.Equal("origin", origin);
        Assert.Equal(channelId, channel);
        Assert.Equal(2, version);
    }

    [Fact]
    public void WebMessagePolicyDecision_Allow_is_allowed()
    {
        var decision = WebMessagePolicyDecision.Allow();

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.DropReason);
    }

    [Fact]
    public void WebMessagePolicyDecision_Deny_has_reason()
    {
        var decision = WebMessagePolicyDecision.Deny(WebMessageDropReason.OriginNotAllowed);

        Assert.False(decision.IsAllowed);
        Assert.Equal(WebMessageDropReason.OriginNotAllowed, decision.DropReason);
    }

    [Fact]
    public void WebMessagePolicyDecision_value_equality()
    {
        var a = WebMessagePolicyDecision.Allow();
        var b = WebMessagePolicyDecision.Allow();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void WebMessage_on_thread_non_rpc_forwards_to_consumer()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });

        WebMessageReceivedEventArgs? received = null;
        core.WebMessageReceived += (_, e) => received = e;

        // Send a non-RPC message (no "jsonrpc" field)
        adapter.RaiseWebMessage("""{"type":"custom","data":"hello"}""", "*", core.ChannelId);

        Assert.NotNull(received);
        Assert.Contains("custom", received!.Body);
    }

    [Fact]
    public void WebMessage_policy_denied_fires_diagnostics()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);

        var drops = new List<WebMessageDropDiagnostic>();
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "https://allowed.com" },
            DropDiagnosticsSink = new TestDropSink(drops)
        });

        // Send from a non-allowed origin
        adapter.RaiseWebMessage("hello", "https://evil.com", core.ChannelId);

        Assert.Single(drops);
    }

    [Fact]
    public void EnableWebMessageBridge_twice_reuses_rpc_service()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });
        var rpc1 = core.Rpc;
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });
        var rpc2 = core.Rpc;
        Assert.Same(rpc1, rpc2);
    }

    [Fact]
    public void Branch_WebMessage_off_thread_dispatches_to_ui()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });

        WebMessageReceivedEventArgs? received = null;
        core.WebMessageReceived += (_, e) => received = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseWebMessage("{\"type\":\"bg\"}", "*", core.ChannelId);
        });

        _dispatcher.RunAll();
        Assert.NotNull(received);
    }

    [Fact]
    public void WebMessageReceived_bridge_not_enabled_drops_message()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        WebMessageReceivedEventArgs? msgArgs = null;
        core.WebMessageReceived += (_, e) => msgArgs = e;

        // Don't enable bridge — message should be dropped
        adapter.RaiseWebMessage("hello", "https://origin.test", Guid.NewGuid());

        Assert.Null(msgArgs);
    }

    [Fact]
    public void WebMessageReceived_after_dispose_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        WebMessageReceivedEventArgs? msgArgs = null;
        core.WebMessageReceived += (_, e) => msgArgs = e;

        core.Dispose();

        adapter.RaiseWebMessage("hello", "https://origin.test", Guid.NewGuid());

        Assert.Null(msgArgs);
    }

    [Fact]
    public void WebMessageReceived_off_thread_then_disposed_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        WebMessageReceivedEventArgs? msgArgs = null;
        core.WebMessageReceived += (_, e) => msgArgs = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseWebMessage("hello", "https://origin.test", Guid.NewGuid());
        });

        core.Dispose();
        _dispatcher.RunAll();

        Assert.Null(msgArgs);
    }

    [Fact]
    public void Rpc_is_null_before_bridge_enabled()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        Assert.Null(core.Rpc);
    }

    [Fact]
    public void Rpc_is_non_null_after_bridge_enabled()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });
        Assert.NotNull(core.Rpc);
    }

    [Fact]
    public void Rpc_disable_bridge_clears_rpc()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });
        Assert.NotNull(core.Rpc);
        core.DisableWebMessageBridge();
        Assert.Null(core.Rpc);
    }

    [Fact]
    public void Rpc_handler_registration_and_removal()
    {
        var rpc = CreateTestRpcService(out _);
        rpc.Handle("test.method", _ => Task.FromResult<object?>(42));

        var request = """{"jsonrpc":"2.0","id":"1","method":"test.method","params":null}""";
        Assert.True(rpc.TryProcessMessage(request));

        rpc.UnregisterHandler("test.method");
        Assert.True(rpc.TryProcessMessage(request));
    }

    [Fact]
    public void Rpc_sync_handler_dispatches_and_sends_response()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("math.add", args =>
        {
            var a = args!.Value.GetProperty("a").GetInt32();
            var b = args!.Value.GetProperty("b").GetInt32();
            return a + b;
        });

        var request = """{"jsonrpc":"2.0","id":"sync-1","method":"math.add","params":{"a":3,"b":4}}""";
        rpc.TryProcessMessage(request);
        WaitUntil(() => scripts.Any(s => s.Contains("sync-1")));
        Assert.Contains(scripts, s => s.Contains("_onResponse") && s.Contains("sync-1"));
    }

    [Fact]
    public void Rpc_async_handler_dispatches()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("async.echo", async args =>
        {
            await Task.Yield();
            return args?.GetProperty("msg").GetString();
        });

        var request = """{"jsonrpc":"2.0","id":"async-1","method":"async.echo","params":{"msg":"hello"}}""";
        rpc.TryProcessMessage(request);
        WaitUntil(() => scripts.Any(s => s.Contains("async-1")));
        Assert.Contains(scripts, s => s.Contains("_onResponse") && s.Contains("async-1"));
    }

    [Fact]
    public void Rpc_unknown_method_sends_error()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var request = """{"jsonrpc":"2.0","id":"unk-1","method":"nonexistent","params":null}""";
        rpc.TryProcessMessage(request);
        WaitUntil(() => scripts.Any(s => s.Contains("-32002") || s.Contains("-32601")));
        Assert.True(scripts.Any(s => s.Contains("-32002") || s.Contains("-32601")), "Expected method-not-found error code");
    }

    [Fact]
    public void Rpc_handler_exception_sends_error()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("bad.handler", _ => throw new InvalidOperationException("Boom"));

        var request = """{"jsonrpc":"2.0","id":"err-1","method":"bad.handler","params":null}""";
        rpc.TryProcessMessage(request);
        WaitUntil(() => scripts.Any(s => s.Contains("Boom")));
        Assert.Contains(scripts, s => s.Contains("Boom"));
    }

    [Fact]
    public async Task Rpc_InvokeAsync_resolves_on_response()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.getTheme", null, TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        WaitUntil(() => scripts.Count > 0);
        Assert.NotEmpty(scripts);
        var callId = ExtractRpcId(scripts[0]);

        var response = "{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":\"dark\"}";
        rpc.TryProcessMessage(response);

        var result = await task;
        Assert.Equal(JsonValueKind.String, result.ValueKind);
        Assert.Equal("dark", result.GetString());
    }

    [Fact]
    public async Task Rpc_InvokeAsync_T_deserializes()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync<int>("js.getCount", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":42}");
        Assert.Equal(42, await task);
    }

    [Fact]
    public async Task Rpc_InvokeAsync_error_throws()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.fail", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"error\":{\"code\":-32603,\"message\":\"JS error\"}}");

        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal(-32603, ex.Code);
    }

    [Fact]
    public async Task Rpc_response_no_result_no_error_sets_default()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.void", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        var json = "{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\"}";
        Assert.True(rpc.TryProcessMessage(json));
        await task; // should complete with default
    }

    [Fact]
    public async Task Rpc_error_no_code_defaults_to_32603()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.x", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        var json = "{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"error\":{\"message\":\"oops\"}}";
        rpc.TryProcessMessage(json);
        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal(-32603, ex.Code);
    }

    [Fact]
    public void Rpc_non_jsonrpc_message_ignored()
    {
        var rpc = CreateTestRpcService(out _);
        Assert.False(rpc.TryProcessMessage("not json"));
        Assert.False(rpc.TryProcessMessage("""{"hello":"world"}"""));
        Assert.False(rpc.TryProcessMessage(""));
    }

    [Fact]
    public void Rpc_orphan_response_not_handled()
    {
        var rpc = CreateTestRpcService(out _);
        Assert.False(rpc.TryProcessMessage("""{"jsonrpc":"2.0","id":"orphan","result":"x"}"""));
    }

    [Fact]
    public void Rpc_request_without_params()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("noparams", args =>
        {
            Assert.Null(args);
            return "ok";
        });

        rpc.TryProcessMessage("""{"jsonrpc":"2.0","id":"np-1","method":"noparams"}""");
        WaitUntil(() => scripts.Any(s => s.Contains("np-1")));
        Assert.Contains(scripts, s => s.Contains("np-1"));
    }

    [Fact]
    public void WebViewRpcException_has_code()
    {
        var ex = new WebViewRpcException(-32601, "Method not found");
        Assert.Equal(-32601, ex.Code);
        Assert.Equal("Method not found", ex.Message);
    }

    [Fact]
    public void Rpc_JsStub_contains_key_identifiers()
    {
        Assert.Contains("agWebView.rpc", WebViewRpcService.JsStub);
        Assert.Contains("invoke", WebViewRpcService.JsStub);
        Assert.Contains("_dispatch", WebViewRpcService.JsStub);
        Assert.Contains("_onResponse", WebViewRpcService.JsStub);
    }

    [Fact]
    public void Rpc_message_routed_through_WebViewCore()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions { AllowedOrigins = new HashSet<string> { "*" } });

        core.Rpc!.Handle("core.ping", _ => "pong");

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"r1","method":"core.ping","params":null}""",
            "*", core.ChannelId);
        _dispatcher.RunAll();
    }

    [Fact]
    public async Task Branch_Rpc_InvokeAsync_with_args_serializes_params()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.greet", new { name = "Alice" }, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        Assert.NotEmpty(scripts);
        var callId = ExtractRpcId(scripts[0]);
        // The script should contain the params
        Assert.Contains("Alice", scripts[0]);

        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":\"Hello Alice\"}");
        var result = await task;
        Assert.Equal("Hello Alice", result.GetString());
    }

    [Fact]
    public void Branch_Rpc_TryProcessMessage_null_id_is_ignored()
    {
        var rpc = CreateTestRpcService(out _);
        // null id — the id property is JSON null
        var result = rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":null,\"method\":\"test\"}");
        // id is null string → _pendingCalls won't match, then method check still runs
        // but method is not null so it dispatches (and finds no handler → sends error)
        Assert.True(result);
    }

    [Fact]
    public void Branch_Rpc_TryProcessMessage_null_method_is_ignored()
    {
        var rpc = CreateTestRpcService(out _);
        // id is present but method is JSON null
        var result = rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"method\":null}");
        // method is null → falls through, returns false
        Assert.False(result);
    }

    [Fact]
    public async Task Branch_Rpc_InvokeAsync_error_without_code_uses_default()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.fail", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        // Error without "code" property — default -32603 should be used
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"error\":{\"message\":\"oops\"}}");

        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal(-32603, ex.Code);
        Assert.Equal("oops", ex.Message);
    }

    [Fact]
    public async Task Branch_Rpc_InvokeAsync_error_without_message_uses_default()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.fail2", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        // Error with code but no "message" — default "RPC error" should be used
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"error\":{\"code\":-1}}");

        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal(-1, ex.Code);
        Assert.Equal("RPC error", ex.Message);
    }

    [Fact]
    public void Branch_Rpc_handler_returns_null_sends_null_result()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("void.method", _ => Task.FromResult<object?>(null));

        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"v1\",\"method\":\"void.method\",\"params\":null}");
        WaitUntil(() => scripts.Any(s => s.Contains("v1")));
        // The response should contain "v1" and null result
        Assert.Contains(scripts, s => s.Contains("v1"));
    }

    [Fact]
    public void Branch_Rpc_TryProcessMessage_malformed_json_returns_false()
    {
        var rpc = CreateTestRpcService(out _);
        Assert.False(rpc.TryProcessMessage("not-json!!!"));
    }

    [Fact]
    public async Task Branch_Rpc_InvokeAsync_T_null_result_returns_default()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync<string>("js.nullResult", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        // Response with null result
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":null}");

        var result = await task;
        Assert.Null(result);
    }

    [Fact]
    public async Task Rpc_InvokeAsync_params_serialize_as_camelCase()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.setProfile", new PlainProfile("Alice", true, 5), TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        Assert.NotEmpty(scripts);

        // Extract the dispatched JSON and verify camelCase property names
        var dispatchScript = scripts[0];
        Assert.Contains("userName", dispatchScript);
        Assert.Contains("isAdmin", dispatchScript);
        Assert.Contains("loginCount", dispatchScript);
        // Ensure PascalCase names are NOT present in the payload
        Assert.DoesNotContain("UserName", dispatchScript);
        Assert.DoesNotContain("IsAdmin", dispatchScript);
        Assert.DoesNotContain("LoginCount", dispatchScript);

        var callId = ExtractRpcId(scripts[0]);
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":null}");
        await task;
    }

    [Fact]
    public void Rpc_handler_result_serializes_as_camelCase()
    {
        var rpc = CreateTestRpcService(out var scripts);
        rpc.Handle("getProfile", _ => Task.FromResult<object?>(new PlainProfile("Bob", false, 10)));

        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"cc-1\",\"method\":\"getProfile\",\"params\":null}");
        WaitUntil(() => scripts.Any(s => s.Contains("cc-1")));

        var responseScript = scripts.First(s => s.Contains("_onResponse") && s.Contains("cc-1"));
        Assert.Contains("userName", responseScript);
        Assert.Contains("isAdmin", responseScript);
        Assert.Contains("loginCount", responseScript);
        Assert.DoesNotContain("UserName", responseScript);
        Assert.DoesNotContain("IsAdmin", responseScript);
        Assert.DoesNotContain("LoginCount", responseScript);
    }

    [Fact]
    public async Task Rpc_InvokeAsync_T_deserializes_camelCase_into_record()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync<PlainProfile>("js.getProfile", null, TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        var callId = ExtractRpcId(scripts[0]);

        // JS returns camelCase JSON
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":{\"userName\":\"Alice\",\"isAdmin\":true,\"loginCount\":5}}");

        var profile = await task;
        Assert.NotNull(profile);
        Assert.Equal("Alice", profile!.UserName);
        Assert.True(profile.IsAdmin);
        Assert.Equal(5, profile.LoginCount);
    }

    [Fact]
    public async Task Rpc_JsonPropertyName_takes_priority_over_naming_policy()
    {
        var rpc = CreateTestRpcService(out var scripts);
        var task = rpc.InvokeAsync("js.setCustomProfile", new CustomNameProfile("Charlie", true), TestContext.Current.CancellationToken);

        WaitUntil(() => scripts.Count > 0);
        Assert.NotEmpty(scripts);

        // user_name from [JsonPropertyName] should appear, not userName
        var dispatchScript = scripts[0];
        Assert.Contains("user_name", dispatchScript);
        // isAdmin should still be camelCase (no [JsonPropertyName] on it)
        Assert.Contains("isAdmin", dispatchScript);

        var callId = ExtractRpcId(scripts[0]);
        rpc.TryProcessMessage("{\"jsonrpc\":\"2.0\",\"id\":\"" + callId + "\",\"result\":null}");
        await task;
    }
}
