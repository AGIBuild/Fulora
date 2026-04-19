using System.Reflection;
using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Shell;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class BranchCoverageRound3Tests
{
    #region Medium: WebViewRpcService notification handlers

    [Fact]
    public async Task HandleNotification_cancelRequest_cancels_active_cts()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerObservedCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        rpc.Handle("slow.op", async (JsonElement? args, CancellationToken ct) =>
        {
            handlerStarted.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                handlerObservedCancellation.TrySetResult(true);
                throw;
            }
            return null;
        });

        // Dispatch a cancellable request, then send $/cancelRequest. The handler must observe the cancellation.
        Assert.True(rpc.TryProcessMessage("""{"jsonrpc":"2.0","id":"req-1","method":"slow.op"}"""));
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var handled = rpc.TryProcessMessage("""{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":"req-1"}}""");
        Assert.True(handled);

        await handlerObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void HandleNotification_cancelRequest_unknown_id_still_returns_true()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 453: targetId is not null but key not found in _activeCancellations
        var cancelJson = """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":"nonexistent"}}""";
        var handled = rpc.TryProcessMessage(cancelJson);
        Assert.True(handled);
    }

    [Fact]
    public void HandleNotification_cancelRequest_null_id_returns_true()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 453: targetId is null → `targetId is not null` fails → skip cancel
        var cancelJson = """{"jsonrpc":"2.0","method":"$/cancelRequest","params":{"id":null}}""";
        var handled = rpc.TryProcessMessage(cancelJson);
        Assert.True(handled);
    }

    [Fact]
    public void HandleNotification_enumeratorAbort_dispatches()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // Line 459: $/enumerator/abort notification
        var abortJson = """{"jsonrpc":"2.0","method":"$/enumerator/abort","params":{"token":"test-token"}}""";
        var handled = rpc.TryProcessMessage(abortJson);
        Assert.True(handled);
    }

    [Fact]
    public async Task ResolvePendingCall_error_without_message_uses_default()
    {
        // Drive the pending-call → error-resolution path through the public surface.
        // Capturing the dispatched script lets us recover the auto-generated id and reply with a synthetic JS-side error.
        string? capturedScript = null;
        var rpc = new WebViewRpcService(
            s => { capturedScript = s; return Task.FromResult<string?>(null); },
            NullLoggerFactory.Instance.CreateLogger("test"));

        var task = rpc.InvokeAsync("test.method", null, TestContext.Current.CancellationToken);
        Assert.NotNull(capturedScript);
        var id = ExtractRpcRequestId(capturedScript!);

        rpc.TryProcessMessage($"{{\"jsonrpc\":\"2.0\",\"id\":\"{id}\",\"error\":{{\"code\":-32600}}}}");

        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal(-32600, ex.Code);
        Assert.Equal("RPC error", ex.Message);
    }

    [Fact]
    public async Task ResolvePendingCall_error_with_null_message_uses_default()
    {
        string? capturedScript = null;
        var rpc = new WebViewRpcService(
            s => { capturedScript = s; return Task.FromResult<string?>(null); },
            NullLoggerFactory.Instance.CreateLogger("test"));

        var task = rpc.InvokeAsync("test.method", null, TestContext.Current.CancellationToken);
        Assert.NotNull(capturedScript);
        var id = ExtractRpcRequestId(capturedScript!);

        // Error with explicit null message → GetString() returns null → ?? "RPC error"
        rpc.TryProcessMessage($"{{\"jsonrpc\":\"2.0\",\"id\":\"{id}\",\"error\":{{\"code\":-32600,\"message\":null}}}}");

        var ex = await Assert.ThrowsAsync<WebViewRpcException>(() => task);
        Assert.Equal("RPC error", ex.Message);
    }

    private static string ExtractRpcRequestId(string dispatchScript)
    {
        // Script form (after JsonSerializer escapes the inner JSON envelope):
        //   ...rpc._dispatch("{\u0022jsonrpc\u0022:\u00222.0\u0022,\u0022id\u0022:\u0022<guid>\u0022,...}")
        // Substring between the first \u0022id\u0022:\u0022 marker and the next \u0022.
        const string idMarker = "\\u0022id\\u0022:\\u0022";
        const string endMarker = "\\u0022";
        var start = dispatchScript.IndexOf(idMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Failed to find id marker in dispatch script: {dispatchScript}");
        start += idMarker.Length;
        var end = dispatchScript.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Failed to find id terminator in dispatch script: {dispatchScript}");
        return dispatchScript.Substring(start, end - start);
    }

    #endregion

    #region Round 4: RPC notification edge cases

    [Fact]
    public void HandleNotification_unknown_method_returns_false()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // A notification with an unknown method should reach line 459 with methodName != "$/enumerator/abort"
        var json = """{"jsonrpc":"2.0","method":"$/some/unknown/method","params":{}}""";
        var handled = rpc.TryProcessMessage(json);
        Assert.False(handled);
    }

    [Fact]
    public void HandleNotification_enumeratorAbort_without_params_falls_through()
    {
        var rpc = new WebViewRpcService(
            s => Task.FromResult<string?>(null),
            NullLoggerFactory.Instance.CreateLogger("test"));

        // $/enumerator/abort without "params" → TryGetProperty("params",...) returns false → falls through
        var json = """{"jsonrpc":"2.0","method":"$/enumerator/abort"}""";
        var handled = rpc.TryProcessMessage(json);
        Assert.False(handled);
    }

    #endregion

    #region Round 5 Tier 2: BridgeImportProxy.Invoke with null args

    [Fact]
    public void BridgeImportProxy_invoke_with_null_args_sends_null_params()
    {
        var proxy = DispatchProxy.Create<INoArgImport, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;

        string? capturedMethod = null;
        object? capturedParams = null;
        var mockRpc = new LambdaRpcService((method, p) =>
        {
            capturedMethod = method;
            capturedParams = p;
            return Task.FromResult(default(JsonElement));
        });
        bridgeProxy.Initialize(mockRpc, "TestSvc");

        var invokeMethod = typeof(BridgeImportProxy)
            .GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var targetMethod = typeof(INoArgImport).GetMethod("DoAsync")!;

        invokeMethod.Invoke(bridgeProxy, [targetMethod, null]);

        Assert.Equal("TestSvc.doAsync", capturedMethod);
        Assert.Null(capturedParams);
    }

    #endregion
}
