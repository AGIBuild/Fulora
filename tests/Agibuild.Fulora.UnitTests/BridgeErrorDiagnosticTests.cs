using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Unit tests for <see cref="BridgeErrorDiagnostic"/> factory methods and error code mapping.
/// </summary>
public sealed class BridgeErrorDiagnosticTests
{
    [Fact]
    public void ServiceNotFound_produces_correct_code_message_and_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.ServiceNotFound("AppService");

        Assert.Equal(BridgeErrorCode.ServiceNotFound, diagnostic.Code);
        Assert.Equal("Service 'AppService' is not registered.", diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains("bridge.Expose", diagnostic.Hint);
        Assert.Contains("IAppService", diagnostic.Hint);
        Assert.Contains("AppService", diagnostic.Hint);
    }

    [Fact]
    public void MethodNotFound_produces_correct_code_message_and_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.MethodNotFound("AppService", "getCurrentUser");

        Assert.Equal(BridgeErrorCode.MethodNotFound, diagnostic.Code);
        Assert.Equal("Method 'getCurrentUser' not found on service 'AppService'.", diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains("[JsExport]", diagnostic.Hint);
        Assert.Contains("AppService", diagnostic.Hint);
        Assert.Contains("getCurrentUser", diagnostic.Hint);
    }

    [Fact]
    public void ParameterMismatch_produces_correct_code_message_and_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.ParameterMismatch("AppService", "save", "Expected int, got string");

        Assert.Equal(BridgeErrorCode.ParameterMismatch, diagnostic.Code);
        Assert.Equal("Parameter mismatch calling 'AppService.save': Expected int, got string", diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains("TypeScript", diagnostic.Hint);
        Assert.Contains("fulora generate", diagnostic.Hint);
    }

    [Fact]
    public void SerializationError_produces_correct_code_message_and_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.SerializationError("AppService", "upload", "Cannot deserialize type X");

        Assert.Equal(BridgeErrorCode.SerializationError, diagnostic.Code);
        Assert.Equal("Serialization error for 'AppService.upload': Cannot deserialize type X", diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains("JSON-serializable", diagnostic.Hint);
        Assert.Contains("AppService", diagnostic.Hint);
        Assert.Contains("upload", diagnostic.Hint);
    }

    [Fact]
    public void InvocationError_produces_correct_code_message_and_null_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.InvocationError("AppService", "process", "Boom!");

        Assert.Equal(BridgeErrorCode.InvocationError, diagnostic.Code);
        Assert.Equal("Error invoking 'AppService.process': Boom!", diagnostic.Message);
        Assert.Null(diagnostic.Hint);
    }

    [Fact]
    public void Timeout_produces_correct_code_message_and_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.Timeout("AppService", "longOperation");

        Assert.Equal(BridgeErrorCode.TimeoutError, diagnostic.Code);
        Assert.Equal("Call to 'AppService.longOperation' timed out.", diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains("timeout", diagnostic.Hint);
        Assert.Contains("AppService", diagnostic.Hint);
        Assert.Contains("longOperation", diagnostic.Hint);
    }

    [Fact]
    public void Cancellation_produces_correct_code_message_and_null_hint()
    {
        var diagnostic = BridgeErrorDiagnostic.Cancellation("AppService", "cancelMe");

        Assert.Equal(BridgeErrorCode.CancellationError, diagnostic.Code);
        Assert.Equal("Call to 'AppService.cancelMe' was cancelled.", diagnostic.Message);
        Assert.Null(diagnostic.Hint);
    }

    [Theory]
    [InlineData("AppService", "getCurrentUser")]
    [InlineData("Api", "ping")]
    [InlineData("CustomName", "doSomething")]
    public void Hint_text_includes_service_and_method_names(string serviceName, string methodName)
    {
        var diagnostic = BridgeErrorDiagnostic.MethodNotFound(serviceName, methodName);

        Assert.Contains(serviceName, diagnostic.Message);
        Assert.Contains(methodName, diagnostic.Message);
        Assert.NotNull(diagnostic.Hint);
        Assert.Contains(serviceName, diagnostic.Hint);
        Assert.Contains(methodName, diagnostic.Hint);
    }

    [Fact]
    public void Hints_are_actionable_and_mention_specific_actions()
    {
        var serviceNotFound = BridgeErrorDiagnostic.ServiceNotFound("X");
        Assert.Contains("Expose", serviceNotFound.Hint);
        Assert.Contains("UsePlugin", serviceNotFound.Hint);

        var methodNotFound = BridgeErrorDiagnostic.MethodNotFound("X", "y");
        Assert.Contains("JsExport", methodNotFound.Hint);
        Assert.Contains("re-exposed", methodNotFound.Hint);

        var paramMismatch = BridgeErrorDiagnostic.ParameterMismatch("X", "y", "details");
        Assert.Contains("TypeScript", paramMismatch.Hint);
        Assert.Contains("fulora generate", paramMismatch.Hint);

        var serialization = BridgeErrorDiagnostic.SerializationError("X", "y", "details");
        Assert.Contains("JSON-serializable", serialization.Hint);
        Assert.Contains("JsonSerializable", serialization.Hint);
    }

    [Theory]
    [InlineData(BridgeErrorCode.ServiceNotFound, -32601)]
    [InlineData(BridgeErrorCode.MethodNotFound, -32601)]
    [InlineData(BridgeErrorCode.ParameterMismatch, -32602)]
    [InlineData(BridgeErrorCode.SerializationError, -32602)]
    [InlineData(BridgeErrorCode.InvocationError, -32603)]
    [InlineData(BridgeErrorCode.TimeoutError, -32603)]
    [InlineData(BridgeErrorCode.CancellationError, -32800)]
    [InlineData(BridgeErrorCode.PermissionDenied, -32603)]
    [InlineData(BridgeErrorCode.RateLimitExceeded, -32029)]
    [InlineData(BridgeErrorCode.Unknown, -32603)]
    public void ToJsonRpcCode_maps_all_error_codes_correctly(BridgeErrorCode code, int expectedRpcCode)
    {
        Assert.Equal(expectedRpcCode, BridgeErrorDiagnostic.ToJsonRpcCode(code));
    }

    [Fact]
    public void Method_not_found_response_includes_diagnostic_when_EnableDevToolsDiagnostics_true()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" },
            EnableDevToolsDiagnostics = true
        });
        var scripts = new List<string>();
        adapter.ScriptCallback = script => { scripts.Add(script); return null; };

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"m-1","method":"NonExistentService.nonExistentMethod","params":{}}""",
            "*", core.ChannelId);
        dispatcher.RunAll();

        var responseScript = scripts.FirstOrDefault(s => s.Contains("_onResponse") && s.Contains("error"));
        Assert.NotNull(responseScript);
        Assert.Contains("-32601", responseScript);
        Assert.Contains("NonExistentService", responseScript);
        Assert.Contains("nonExistentMethod", responseScript);
        Assert.Contains("diagnosticCode", responseScript);
        Assert.Contains("1002", responseScript);
        Assert.Contains("hint", responseScript);
        Assert.Contains("JsExport", responseScript);
    }

    [Fact]
    public void Method_not_found_response_omits_hint_when_EnableDevToolsDiagnostics_false()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" },
            EnableDevToolsDiagnostics = false
        });
        var scripts = new List<string>();
        adapter.ScriptCallback = script => { scripts.Add(script); return null; };

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"m-1","method":"NonExistentService.nonExistentMethod","params":{}}""",
            "*", core.ChannelId);
        dispatcher.RunAll();

        var responseScript = scripts.FirstOrDefault(s => s.Contains("_onResponse") && s.Contains("error"));
        Assert.NotNull(responseScript);
        Assert.Contains("diagnosticCode", responseScript);
        Assert.Contains("1002", responseScript);
        Assert.DoesNotContain("hint", responseScript);
    }

    [Fact]
    public void WebMessage_policy_drop_emits_unified_runtime_diagnostic_event()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, dispatcher);
        var sink = new MemoryFuloraDiagnosticsSink();
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "https://allowed.example" },
            DiagnosticsSink = sink
        });

        adapter.RaiseWebMessage(
            """{"jsonrpc":"2.0","id":"m-1","method":"AppService.ping","params":{}}""",
            "https://blocked.example",
            core.ChannelId);
        dispatcher.RunAll();

        var diagnostic = Assert.Single(sink.Events);
        Assert.Equal("runtime.webmessage.dropped", diagnostic.EventName);
        Assert.Equal("runtime", diagnostic.Layer);
        Assert.Equal("WebViewCore", diagnostic.Component);
        Assert.Equal(core.ChannelId.ToString("D"), diagnostic.ChannelId);
        Assert.Equal("dropped", diagnostic.Status);
        Assert.Equal("OriginNotAllowed", diagnostic.ErrorType);
        Assert.Equal("https://blocked.example", diagnostic.Attributes["origin"]);
        Assert.Equal("OriginNotAllowed", diagnostic.Attributes["dropReason"]);
    }
}
