using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Agibuild.Fulora.Rpc;

/// <summary>
/// Manages the lifecycle of in-flight C# → JS RPC calls. For each call this
/// component:
/// <list type="number">
///   <item>Allocates a request id and a <see cref="TaskCompletionSource{TResult}"/>.</item>
///   <item>Serialises the request envelope and pushes it to the JS runtime
///         via the supplied <c>invokeScript</c> delegate.</item>
///   <item>Waits for either a JS-side response (resolved by
///         <see cref="TryResolve"/> from the dispatcher), a 30 s timeout, or
///         a caller-supplied cancellation token.</item>
/// </list>
/// On caller cancellation, a best-effort <c>$/cancelRequest</c> notification
/// is sent through the same <c>invokeScript</c> delegate to give the JS side a
/// chance to abort. The 30 s timeout is the contractual upper bound; tests
/// that need a tighter bound should drive the underlying script delegate.
/// </summary>
internal sealed class RpcPendingCallCoordinator
{
    private static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingCalls = new();
    private readonly Func<string, Task<string?>> _invokeScript;

    public RpcPendingCallCoordinator(Func<string, Task<string?>> invokeScript)
    {
        _invokeScript = invokeScript;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC args serialization uses runtime types; callers are responsible for ensuring types are preserved.")]
    public async Task<JsonElement> InvokeAsync(string method, object? args)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCalls[id] = tcs;

        try
        {
            await DispatchRequestAsync(id, method, args);

            using var cts = new CancellationTokenSource(InvokeTimeout);
            cts.Token.Register(() => tcs.TrySetException(
                new WebViewRpcException(-32000, $"RPC call '{method}' timed out.")));

            return await tcs.Task;
        }
        finally
        {
            _pendingCalls.TryRemove(id, out _);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Generic RPC deserialization; callers are responsible for ensuring T is preserved.")]
    public async Task<T?> InvokeAsync<T>(string method, object? args)
    {
        var result = await InvokeAsync(method, args);
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return default;
        return result.Deserialize<T>(RpcResultSerializer.BridgeJsonOptions);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC args serialization uses runtime types.")]
    public async Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        cancellationToken.ThrowIfCancellationRequested();

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCalls[id] = tcs;

        CancellationTokenRegistration? ctReg = null;
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                ctReg = cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled(cancellationToken);
                    _ = SendCancelRequestAsync(id);
                });
            }

            await DispatchRequestAsync(id, method, args);

            using var timeoutCts = new CancellationTokenSource(InvokeTimeout);
            timeoutCts.Token.Register(() => tcs.TrySetException(
                new WebViewRpcException(-32000, $"RPC call '{method}' timed out.")));

            return await tcs.Task;
        }
        finally
        {
            ctReg?.Dispose();
            _pendingCalls.TryRemove(id, out _);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Generic RPC deserialization.")]
    public async Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync(method, args, cancellationToken);
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return default;
        return result.Deserialize<T>(RpcResultSerializer.BridgeJsonOptions);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC notification args serialization uses runtime types.")]
    public async Task NotifyAsync(string method, object? args)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var notification = new RpcRequest
        {
            Id = null,
            Method = method,
            Params = args is null ? null : JsonSerializer.SerializeToElement(args, RpcResultSerializer.BridgeJsonOptions)
        };

        await DispatchEnvelopeAsync(notification);
    }

    /// <summary>
    /// Resolves a pending call when the dispatcher recognises an RPC response
    /// for the given <paramref name="id"/>. Returns <c>false</c> if no such
    /// call is pending (orphan response).
    /// </summary>
    public bool TryResolve(string id, JsonElement responseRoot)
    {
        if (!_pendingCalls.TryRemove(id, out var tcs))
            return false;

        if (responseRoot.TryGetProperty("error", out var errorProp))
        {
            var code = errorProp.TryGetProperty("code", out var c) ? c.GetInt32() : -32603;
            var msg = errorProp.TryGetProperty("message", out var m) ? m.GetString() ?? "RPC error" : "RPC error";
            tcs.TrySetException(new WebViewRpcException(code, msg));
        }
        else if (responseRoot.TryGetProperty("result", out var resultProp))
        {
            tcs.TrySetResult(resultProp.Clone());
        }
        else
        {
            tcs.TrySetResult(default);
        }

        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC args serialization uses runtime types.")]
    private async Task DispatchRequestAsync(string id, string method, object? args)
    {
        var request = new RpcRequest
        {
            Id = id,
            Method = method,
            Params = args is null ? null : JsonSerializer.SerializeToElement(args, RpcResultSerializer.BridgeJsonOptions)
        };

        await DispatchEnvelopeAsync(request);
    }

    private async Task DispatchEnvelopeAsync(RpcRequest envelope)
    {
        var json = JsonSerializer.Serialize(envelope, RpcJsonContext.Default.RpcRequest);
        var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
        await _invokeScript(script);
    }

    private async Task SendCancelRequestAsync(string requestId)
    {
        try
        {
            await NotifyAsync("$/cancelRequest", new { id = requestId });
        }
        catch
        {
            // Best-effort cancel notification — JS side may already be torn down.
        }
    }
}
