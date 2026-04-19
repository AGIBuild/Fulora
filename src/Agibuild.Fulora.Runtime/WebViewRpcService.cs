using System.Text.Json;
using Agibuild.Fulora.Rpc;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// JSON-RPC 2.0 service for bidirectional JS ↔ C# method calls over the
/// WebMessage bridge.
/// <para>
/// This type is the public-facing facade. The actual state machines and
/// serialization live in the <c>Agibuild.Fulora.Rpc</c> namespace and are
/// composed here:
/// </para>
/// <list type="bullet">
///   <item><see cref="RpcHandlerRegistry"/> — JS → C# dispatch table.</item>
///   <item><see cref="RpcPendingCallCoordinator"/> — C# → JS calls
///         (timeouts, cancellation propagation, response resolution).</item>
///   <item><see cref="RpcCancellationCoordinator"/> — server-side cancellation
///         state machine for <c>$/cancelRequest</c>.</item>
///   <item><see cref="RpcEnumeratorRegistry"/> — server-side iterator
///         lifecycle for <c>$/enumerator/next/{token}</c> and
///         <c>$/enumerator/abort</c>.</item>
///   <item><see cref="RpcResultSerializer"/> — AOT-safe success/error envelope
///         serialization.</item>
/// </list>
/// The coordinator only owns the message dispatch pipeline (parse →
/// route response/notification/request → run handler → ship response).
/// </summary>
internal sealed class WebViewRpcService : IWebViewRpcService
{
    /// <summary>
    /// JS stub injected into every WebView page. Re-exported here for callers
    /// that already reference <see cref="WebViewRpcService"/>; the source of
    /// truth is <see cref="RpcJsStub.JsStub"/>.
    /// </summary>
    internal const string JsStub = RpcJsStub.JsStub;

    /// <summary>
    /// Maximum idle window between <c>$/enumerator/next</c> calls before the
    /// enumerator is auto-disposed. Re-exported from
    /// <see cref="RpcEnumeratorRegistry.InactivityTimeout"/> for tests that
    /// assert on the contract.
    /// </summary>
    internal static TimeSpan EnumeratorInactivityTimeout => RpcEnumeratorRegistry.InactivityTimeout;

    private readonly Func<string, Task<string?>> _invokeScript;
    private readonly ILogger _logger;
    private readonly RpcHandlerRegistry _handlers;
    private readonly RpcPendingCallCoordinator _pendingCalls;
    private readonly RpcCancellationCoordinator _cancellations;
    private readonly RpcEnumeratorRegistry _enumerators;
    private readonly RpcResultSerializer _serializer;

    internal WebViewRpcService(Func<string, Task<string?>> invokeScript, ILogger logger, bool enableDevToolsDiagnostics = false)
    {
        _invokeScript = invokeScript;
        _logger = logger;
        _handlers = new RpcHandlerRegistry();
        _pendingCalls = new RpcPendingCallCoordinator(invokeScript);
        _cancellations = new RpcCancellationCoordinator();
        _enumerators = new RpcEnumeratorRegistry(_handlers, logger);
        _serializer = new RpcResultSerializer(enableDevToolsDiagnostics);
    }

    // ==================== IWebViewRpcService — handler registration ====================

    public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
        => _handlers.Handle(method, handler);

    public void Handle(string method, Func<JsonElement?, object?> handler)
        => _handlers.Handle(method, handler);

    public void Handle(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _handlers.Handle(method, handler);

    public void UnregisterHandler(string method)
        => _handlers.Unregister(method);

    // ==================== IWebViewRpcService — enumerator registration ====================

    public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose)
        => _enumerators.Register(token, moveNext, dispose);

    internal Task DisposeEnumerator(string token)
        => _enumerators.DisposeAsync(token);

    // ==================== IWebViewRpcService — C# → JS calls ====================

    public Task<JsonElement> InvokeAsync(string method, object? args = null)
        => _pendingCalls.InvokeAsync(method, args);

    public Task<T?> InvokeAsync<T>(string method, object? args = null)
        => _pendingCalls.InvokeAsync<T>(method, args);

    public Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken cancellationToken)
        => _pendingCalls.InvokeAsync(method, args, cancellationToken);

    public Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken cancellationToken)
        => _pendingCalls.InvokeAsync<T>(method, args, cancellationToken);

    public Task NotifyAsync(string method, object? args = null)
        => _pendingCalls.NotifyAsync(method, args);

    // ==================== Test-visible cancellation hooks ====================

    internal void RegisterCancellation(string requestId, CancellationTokenSource cts)
        => _cancellations.Register(requestId, cts);

    internal void UnregisterCancellation(string requestId)
        => _cancellations.Unregister(requestId);

    // ==================== JS → C# dispatch pipeline ====================

    /// <summary>
    /// Called by <c>WebViewCoreBridgeRuntime</c> when a WebMessage with an RPC
    /// envelope is received. Returns <c>true</c> if the message was handled
    /// (request, notification, response, or batch). Non-RPC payloads return
    /// <c>false</c> so the caller can route them elsewhere.
    /// </summary>
    internal bool TryProcessMessage(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                _ = ProcessBatchAsync(root.Clone());
                return true;
            }

            if (!root.TryGetProperty("jsonrpc", out var ver) || ver.GetString() != "2.0")
                return false;

            if (!root.TryGetProperty("id", out var idProp))
                return HandleNotification(root);

            var id = idProp.GetString();
            if (id is not null && _pendingCalls.TryResolve(id, root))
                return true;

            if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                if (method is not null)
                {
                    if (id is not null)
                    {
                        // Mark request as active before scheduling async dispatch so early cancel notifications are not lost.
                        _cancellations.MarkActive(id);
                    }
                    _ = DispatchRequestAsync(id, method, root);
                    return true;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogParseMessageFailed(ex);
        }

        return false;
    }

    private async Task DispatchRequestAsync(string? id, string method, JsonElement root)
    {
        var responseJson = id is null
            ? await DispatchRequestCoreAsync(id, method, root)
            : await DispatchTrackedRequestCoreAsync(id, method, root);
        await SendResponseAsync(responseJson);
    }

    private async Task<string> DispatchTrackedRequestCoreAsync(string id, string method, JsonElement root)
    {
        try
        {
            return await DispatchRequestCoreAsync(id, method, root);
        }
        finally
        {
            _cancellations.ClearActive(id);
        }
    }

    private async Task<string> DispatchRequestCoreAsync(string? id, string method, JsonElement root)
    {
        JsonElement? paramsProp = root.TryGetProperty("params", out var p) ? p.Clone() : null;

        try
        {
            if (_handlers.TryGetCancellable(method, out var cancellableHandler))
            {
                using var cts = new CancellationTokenSource();
                if (id is not null)
                {
                    _cancellations.Register(id, cts);
                    try
                    {
                        var result = await cancellableHandler(paramsProp, cts.Token);
                        return _serializer.BuildSuccessResponseJson(id, result);
                    }
                    finally
                    {
                        _cancellations.Unregister(id);
                    }
                }

                // For malformed requests with JSON null id, do not participate in cancellation tracking.
                var nullIdResult = await cancellableHandler(paramsProp, CancellationToken.None);
                return _serializer.BuildSuccessResponseJson(id, nullIdResult);
            }

            if (!_handlers.TryGet(method, out var handler))
            {
                var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
                var diagnostic = BridgeErrorDiagnostic.MethodNotFound(serviceName, methodName);
                return _serializer.BuildErrorResponseJson(id, diagnostic);
            }

            var handlerResult = await handler(paramsProp);
            return _serializer.BuildSuccessResponseJson(id, handlerResult);
        }
        catch (OperationCanceledException)
        {
            _logger.LogHandlerCancelled(method);
            var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
            return _serializer.BuildErrorResponseJson(id, BridgeErrorDiagnostic.Cancellation(serviceName, methodName));
        }
        catch (WebViewRpcException rpcEx)
        {
            _logger.LogHandlerRpcError(rpcEx, method, rpcEx.Code);
            if (rpcEx.Code == -32029)
            {
                var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
                return _serializer.BuildErrorResponseJson(id, new BridgeErrorDiagnostic(BridgeErrorCode.RateLimitExceeded, rpcEx.Message, null));
            }
            return RpcResultSerializer.BuildErrorResponseJson(id, rpcEx.Code, rpcEx.Message);
        }
        catch (FuloraException fuloraEx) when (fuloraEx is not WebViewRpcException)
        {
            _logger.LogHandlerFuloraException(fuloraEx, method, fuloraEx.ErrorCode);
            var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
            return _serializer.BuildErrorResponseJson(id, BridgeErrorDiagnostic.InvocationError(serviceName, methodName, $"[{fuloraEx.ErrorCode}] {fuloraEx.Message}"));
        }
        catch (JsonException ex)
        {
            _logger.LogHandlerDeserializeFailed(ex, method);
            var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
            return _serializer.BuildErrorResponseJson(id, BridgeErrorDiagnostic.SerializationError(serviceName, methodName, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogHandlerThrew(ex, method);
            var (serviceName, methodName) = RpcMethodHelpers.SplitRpcMethod(method);
            return _serializer.BuildErrorResponseJson(id, BridgeErrorDiagnostic.InvocationError(serviceName, methodName, ex.Message));
        }
    }

    private async Task SendResponseAsync(string json)
    {
        var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._onResponse({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
        await _invokeScript(script);
    }

    // ==================== Batch dispatch ====================

    private async Task ProcessBatchAsync(JsonElement batchArray)
    {
        var tasks = new List<Task<string?>>();
        foreach (var element in batchArray.EnumerateArray())
        {
            tasks.Add(ProcessBatchElementAsync(element));
        }

        var responses = await Task.WhenAll(tasks);
        var nonNull = responses.Where(r => r is not null).ToArray();
        if (nonNull.Length == 0) return;

        var batchJson = "[" + string.Join(",", nonNull) + "]";
        await SendResponseAsync(batchJson);
    }

    private async Task<string?> ProcessBatchElementAsync(JsonElement root)
    {
        var elementId = root.TryGetProperty("id", out var rawId) ? rawId.GetString() : null;

        if (!root.TryGetProperty("jsonrpc", out var ver) || ver.GetString() != "2.0")
            return RpcResultSerializer.BuildErrorResponseJson(elementId, -32600, "Invalid Request");

        if (elementId is null && !root.TryGetProperty("id", out _))
        {
            HandleNotification(root);
            return null;
        }

        var id = elementId;

        if (id is not null && _pendingCalls.TryResolve(id, root))
            return null;

        if (root.TryGetProperty("method", out var methodProp))
        {
            var method = methodProp.GetString();
            if (method is not null && id is not null)
            {
                // Keep cancellation visibility consistent with the single-request dispatch path.
                _cancellations.MarkActive(id);
                return await DispatchTrackedRequestCoreAsync(id, method, root);
            }
        }

        return RpcResultSerializer.BuildErrorResponseJson(id, -32600, "Invalid Request");
    }

    // ==================== Notification routing ====================

    private bool HandleNotification(JsonElement root)
    {
        if (!root.TryGetProperty("method", out var notifMethod))
            return false;

        var methodName = notifMethod.GetString();

        if (methodName == "$/cancelRequest" && root.TryGetProperty("params", out var cancelParams))
        {
            if (cancelParams.TryGetProperty("id", out var cancelId))
            {
                var targetId = cancelId.GetString();
                if (targetId is not null)
                {
                    _cancellations.HandleCancelRequest(targetId);
                }
            }
            return true;
        }

        if (methodName == "$/enumerator/abort" && root.TryGetProperty("params", out var abortParams))
        {
            if (abortParams.TryGetProperty("token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                if (token is not null)
                    _ = _enumerators.DisposeAsync(token);
            }
            return true;
        }

        return false;
    }
}
