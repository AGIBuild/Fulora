using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// JSON-RPC 2.0 service for bidirectional JS ↔ C# method calls over the WebMessage bridge.
/// </summary>
internal sealed class WebViewRpcService : IWebViewRpcService
{
    /// <summary>
    /// Shared JSON options for bridge payload serialization: camelCase naming + case-insensitive deserialization.
    /// RPC envelope types (RpcRequest, RpcResponse, etc.) use source-generated RpcJsonContext and are unaffected.
    /// </summary>
    private static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();
    private readonly ConcurrentDictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _cancellableHandlers = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingCalls = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCancellations = new();
    private readonly ConcurrentDictionary<string, byte> _activeRequestIds = new();
    private readonly ConcurrentDictionary<string, byte> _pendingCancellationRequests = new();
    private readonly ConcurrentDictionary<string, ActiveEnumerator> _activeEnumerators = new();
    private readonly Func<string, Task<string?>> _invokeScript;
    private readonly ILogger _logger;
    private readonly bool _enableDevToolsDiagnostics;

    internal WebViewRpcService(Func<string, Task<string?>> invokeScript, ILogger logger, bool enableDevToolsDiagnostics = false)
    {
        _invokeScript = invokeScript;
        _logger = logger;
        _enableDevToolsDiagnostics = enableDevToolsDiagnostics;
    }

    // ==================== Handler registration ====================

    public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[method] = handler;
    }

    public void Handle(string method, Func<JsonElement?, object?> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[method] = args => Task.FromResult(handler(args));
    }

    public void Handle(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentNullException.ThrowIfNull(handler);
        _cancellableHandlers[method] = handler;
        _handlers[method] = args => handler(args, CancellationToken.None);
    }

    public void UnregisterHandler(string method)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        _handlers.TryRemove(method, out _);
        _cancellableHandlers.TryRemove(method, out _);
    }

    // ==================== Cancellation support ====================

    internal void RegisterCancellation(string requestId, CancellationTokenSource cts)
    {
        _activeCancellations[requestId] = cts;
        if (_pendingCancellationRequests.TryRemove(requestId, out _))
        {
            cts.Cancel();
        }
    }

    internal void UnregisterCancellation(string requestId)
    {
        _pendingCancellationRequests.TryRemove(requestId, out _);
        if (_activeCancellations.TryRemove(requestId, out var cts))
        {
            cts.Dispose();
        }
    }

    // ==================== Enumerator support ====================

    internal static readonly TimeSpan EnumeratorInactivityTimeout = TimeSpan.FromSeconds(30);

    public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose)
    {
        var enumerator = new ActiveEnumerator(moveNext, dispose);
        _activeEnumerators[token] = enumerator;
        enumerator.StartInactivityTimer(EnumeratorInactivityTimeout, () => DisposeEnumerator(token));

        _handlers[$"$/enumerator/next/{token}"] = async (JsonElement? args) =>
        {
            if (_activeEnumerators.TryGetValue(token, out var e))
            {
                e.ResetInactivityTimer(EnumeratorInactivityTimeout, () => DisposeEnumerator(token));

                var (value, finished) = await e.MoveNext();
                if (finished)
                {
                    await DisposeEnumerator(token);
                }
                return new EnumeratorNextResult { Values = finished ? [] : [value], Finished = finished };
            }
            return new EnumeratorNextResult { Values = [], Finished = true };
        };
    }

    internal async Task DisposeEnumerator(string token)
    {
        if (_activeEnumerators.TryRemove(token, out var enumerator))
        {
            enumerator.Dispose();
            _handlers.TryRemove($"$/enumerator/next/{token}", out _);
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RPC: failed to dispose enumerator {Token}", token);
            }
        }
    }

    // ==================== C# → JS calls ====================

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC args serialization uses runtime types; callers are responsible for ensuring types are preserved.")]
    public async Task<JsonElement> InvokeAsync(string method, object? args = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCalls[id] = tcs;

        try
        {
            var request = new RpcRequest
            {
                Id = id,
                Method = method,
                Params = args is null ? null : JsonSerializer.SerializeToElement(args, BridgeJsonOptions)
            };

            var json = JsonSerializer.Serialize(request, RpcJsonContext.Default.RpcRequest);
            // Send via injected JS runtime: window.agWebView.rpc._dispatch(json)
            var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
            await _invokeScript(script);

            // Wait for response (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
    public async Task<T?> InvokeAsync<T>(string method, object? args = null)
    {
        var result = await InvokeAsync(method, args);
        if (result.ValueKind == JsonValueKind.Null || result.ValueKind == JsonValueKind.Undefined)
            return default;
        return result.Deserialize<T>(BridgeJsonOptions);
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

            var request = new RpcRequest
            {
                Id = id,
                Method = method,
                Params = args is null ? null : JsonSerializer.SerializeToElement(args, BridgeJsonOptions)
            };

            var json = JsonSerializer.Serialize(request, RpcJsonContext.Default.RpcRequest);
            var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
            await _invokeScript(script);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
        return result.Deserialize<T>(BridgeJsonOptions);
    }

    private async Task SendCancelRequestAsync(string requestId)
    {
        try
        {
            await NotifyAsync("$/cancelRequest", new { id = requestId });
        }
        catch
        {
            // Best-effort cancel notification
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC notification args serialization uses runtime types.")]
    public async Task NotifyAsync(string method, object? args = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);

        var notification = new RpcRequest
        {
            Id = null,
            Method = method,
            Params = args is null ? null : JsonSerializer.SerializeToElement(args, BridgeJsonOptions)
        };

        var json = JsonSerializer.Serialize(notification, RpcJsonContext.Default.RpcRequest);
        var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._dispatch({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
        await _invokeScript(script);
    }

    // ==================== JS → C# dispatch ====================

    /// <summary>
    /// Called by WebViewCore when a WebMessage with RPC envelope is received.
    /// Returns true if the message was handled as an RPC message.
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

            {
                var id = idProp.GetString();
                if (id is not null && _pendingCalls.TryRemove(id, out var tcs))
                {
                    ResolvePendingCall(root, tcs);
                    return true;
                }

                if (root.TryGetProperty("method", out var methodProp))
                {
                    var method = methodProp.GetString();
                    if (method is not null)
                    {
                        if (id is not null)
                        {
                            // Mark request as active before scheduling async dispatch so early cancel notifications are not lost.
                            _activeRequestIds[id] = 0;
                        }
                        _ = DispatchRequestAsync(id, method, root);
                        return true;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "RPC: failed to parse message");
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
        _activeRequestIds[id] = 0;
        try
        {
            return await DispatchRequestCoreAsync(id, method, root);
        }
        finally
        {
            _activeRequestIds.TryRemove(id, out _);
            _pendingCancellationRequests.TryRemove(id, out _);
        }
    }

    private async Task<string> DispatchRequestCoreAsync(string? id, string method, JsonElement root)
    {
        JsonElement? paramsProp = root.TryGetProperty("params", out var p) ? p.Clone() : null;

        try
        {
            if (_cancellableHandlers.TryGetValue(method, out var cancellableHandler))
            {
                using var cts = new CancellationTokenSource();
                if (id is not null)
                {
                    RegisterCancellation(id, cts);
                    try
                    {
                        var result = await cancellableHandler(paramsProp, cts.Token);
                        return BuildSuccessResponseJson(id, result);
                    }
                    finally
                    {
                        UnregisterCancellation(id);
                    }
                }

                // For malformed requests with JSON null id, do not participate in cancellation tracking.
                var nullIdResult = await cancellableHandler(paramsProp, CancellationToken.None);
                return BuildSuccessResponseJson(id, nullIdResult);
            }

            if (!_handlers.TryGetValue(method, out var handler))
            {
                var (serviceName, methodName) = SplitRpcMethod(method);
                var diagnostic = BridgeErrorDiagnostic.MethodNotFound(serviceName, methodName);
                return BuildErrorResponseJson(id, diagnostic);
            }

            var handlerResult = await handler(paramsProp);
            return BuildSuccessResponseJson(id, handlerResult);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("RPC: handler for '{Method}' was cancelled", method);
            var (serviceName, methodName) = SplitRpcMethod(method);
            return BuildErrorResponseJson(id, BridgeErrorDiagnostic.Cancellation(serviceName, methodName));
        }
        catch (WebViewRpcException rpcEx)
        {
            _logger.LogDebug(rpcEx, "RPC: handler for '{Method}' threw RPC error {Code}", method, rpcEx.Code);
            if (rpcEx.Code == -32029)
            {
                var (serviceName, methodName) = SplitRpcMethod(method);
                return BuildErrorResponseJson(id, new BridgeErrorDiagnostic(BridgeErrorCode.RateLimitExceeded, rpcEx.Message, null));
            }
            return BuildErrorResponseJson(id, rpcEx.Code, rpcEx.Message);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "RPC: handler for '{Method}' failed to deserialize", method);
            var (serviceName, methodName) = SplitRpcMethod(method);
            return BuildErrorResponseJson(id, BridgeErrorDiagnostic.SerializationError(serviceName, methodName, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RPC: handler for '{Method}' threw", method);
            var (serviceName, methodName) = SplitRpcMethod(method);
            return BuildErrorResponseJson(id, BridgeErrorDiagnostic.InvocationError(serviceName, methodName, ex.Message));
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC result serialization uses runtime types; the handler is responsible for type safety.")]
    private static string BuildSuccessResponseJson(string? id, object? result)
    {
        var response = new RpcResponse
        {
            Id = id,
            Result = result is null ? null : JsonSerializer.SerializeToElement(result, BridgeJsonOptions)
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcResponse);
    }

    private static string BuildErrorResponseJson(string? id, int code, string message)
    {
        var response = new RpcErrorResponse
        {
            Id = id,
            Error = new RpcError { Code = code, Message = message }
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcErrorResponse);
    }

    private string BuildErrorResponseJson(string? id, BridgeErrorDiagnostic diagnostic)
    {
        var jsonRpcCode = BridgeErrorDiagnostic.ToJsonRpcCode(diagnostic.Code);
        var data = _enableDevToolsDiagnostics && diagnostic.Hint is not null
            ? new RpcErrorData { DiagnosticCode = (int)diagnostic.Code, Hint = diagnostic.Hint }
            : new RpcErrorData { DiagnosticCode = (int)diagnostic.Code };
        var response = new RpcErrorResponse
        {
            Id = id,
            Error = new RpcError { Code = jsonRpcCode, Message = diagnostic.Message, Data = data }
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcErrorResponse);
    }

    private static (string serviceName, string methodName) SplitRpcMethod(string rpcMethod)
    {
        var dot = rpcMethod.LastIndexOf('.');
        return dot >= 0 ? (rpcMethod[..dot], rpcMethod[(dot + 1)..]) : (rpcMethod, "");
    }

    private async Task SendResponseAsync(string json)
    {
        var script = $"window.agWebView && window.agWebView.rpc && window.agWebView.rpc._onResponse({JsonSerializer.Serialize(json, RpcJsonContext.Default.String)})";
        await _invokeScript(script);
    }

    // ==================== Batch RPC ====================

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
            return BuildErrorResponseJson(elementId, -32600, "Invalid Request");

        if (elementId is null && !root.TryGetProperty("id", out _))
        {
            HandleNotification(root);
            return null;
        }

        var id = elementId;

        if (id is not null && _pendingCalls.TryRemove(id, out var tcs))
        {
            ResolvePendingCall(root, tcs);
            return null;
        }

        if (root.TryGetProperty("method", out var methodProp))
        {
            var method = methodProp.GetString();
            if (method is not null && id is not null)
            {
                // Keep cancellation visibility consistent with single-request dispatch path.
                _activeRequestIds[id] = 0;
                return await DispatchTrackedRequestCoreAsync(id, method, root);
            }
        }

        return BuildErrorResponseJson(id, -32600, "Invalid Request");
    }

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
                    if (_activeCancellations.TryGetValue(targetId, out var cts))
                    {
                        cts.Cancel();
                    }
                    else if (_activeRequestIds.ContainsKey(targetId))
                    {
                        // Cancellation may arrive before CTS registration; defer until handler registration.
                        _pendingCancellationRequests[targetId] = 0;
                    }
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
                    _ = DisposeEnumerator(token);
            }
            return true;
        }

        return false;
    }

    private static void ResolvePendingCall(JsonElement root, TaskCompletionSource<JsonElement> tcs)
    {
        if (root.TryGetProperty("error", out var errorProp))
        {
            var code = errorProp.TryGetProperty("code", out var c) ? c.GetInt32() : -32603;
            var msg = errorProp.TryGetProperty("message", out var m) ? m.GetString() ?? "RPC error" : "RPC error";
            tcs.TrySetException(new WebViewRpcException(code, msg));
        }
        else if (root.TryGetProperty("result", out var resultProp))
        {
            tcs.TrySetResult(resultProp.Clone());
        }
        else
        {
            tcs.TrySetResult(default);
        }
    }

    // ==================== JS stub injection ====================

    internal const string JsStub = """
        (function() {
            if (window.agWebView && window.agWebView.rpc) return;
            if (!window.agWebView) window.agWebView = {};
            var pending = {};
            var handlers = {};
            var nextId = 0;
            function post(msg) {
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(msg);
                } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.agibuildWebView) {
                    window.webkit.messageHandlers.agibuildWebView.postMessage(msg);
                }
            }
            window.agWebView.rpc = {
                _uint8ToBase64: function(bytes) {
                    var binary = '';
                    for (var i = 0; i < bytes.length; i++) {
                        binary += String.fromCharCode(bytes[i]);
                    }
                    return btoa(binary);
                },
                _base64ToUint8: function(base64) {
                    var binary = atob(base64 || '');
                    var bytes = new Uint8Array(binary.length);
                    for (var i = 0; i < binary.length; i++) {
                        bytes[i] = binary.charCodeAt(i);
                    }
                    return bytes;
                },
                _encodeBinaryPayload: function(value) {
                    if (value === null || value === undefined) return value;
                    if (value instanceof Uint8Array) {
                        return window.agWebView.rpc._uint8ToBase64(value);
                    }
                    if (Array.isArray(value)) {
                        var mapped = new Array(value.length);
                        for (var i = 0; i < value.length; i++) {
                            mapped[i] = window.agWebView.rpc._encodeBinaryPayload(value[i]);
                        }
                        return mapped;
                    }
                    if (typeof value === 'object') {
                        var result = {};
                        for (var key in value) {
                            if (Object.prototype.hasOwnProperty.call(value, key)) {
                                result[key] = window.agWebView.rpc._encodeBinaryPayload(value[key]);
                            }
                        }
                        return result;
                    }
                    return value;
                },
                _decodeBinaryResult: function(value) {
                    if (typeof value !== 'string') return value;
                    return window.agWebView.rpc._base64ToUint8(value);
                },
                invoke: function(method, params, signal) {
                    return new Promise(function(resolve, reject) {
                        var id = '__js_' + (nextId++);
                        pending[id] = { resolve: resolve, reject: reject };
                        var encodedParams = window.agWebView.rpc._encodeBinaryPayload(params);
                        post(JSON.stringify({ jsonrpc: '2.0', id: id, method: method, params: encodedParams }));
                        if (signal) {
                            var onAbort = function() {
                                post(JSON.stringify({ jsonrpc: '2.0', method: '$/cancelRequest', params: { id: id } }));
                            };
                            if (signal.aborted) {
                                onAbort();
                            } else {
                                signal.addEventListener('abort', onAbort, { once: true });
                            }
                        }
                    });
                },
                handle: function(method, handler) {
                    handlers[method] = handler;
                },
                _dispatch: function(jsonStr) {
                    var msg = JSON.parse(jsonStr);
                    if (msg.method && handlers[msg.method]) {
                        try {
                            var result = handlers[msg.method](msg.params);
                            if (msg.id == null) return;
                            if (result && typeof result.then === 'function') {
                                result.then(function(r) {
                                    post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: r }));
                                }).catch(function(e) {
                                    post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, error: { code: -32603, message: e.message || 'Error' } }));
                                });
                            } else {
                                post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: result }));
                            }
                        } catch(e) {
                            if (msg.id != null) post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, error: { code: -32603, message: e.message || 'Error' } }));
                        }
                    }
                },
                batch: function(calls) {
                    var requests = [];
                    var ids = [];
                    for (var i = 0; i < calls.length; i++) {
                        var id = '__js_' + (nextId++);
                        ids.push(id);
                        requests.push({
                            jsonrpc: '2.0',
                            id: id,
                            method: calls[i].method,
                            params: window.agWebView.rpc._encodeBinaryPayload(calls[i].params)
                        });
                    }
                    var resultPromises = ids.map(function(id) {
                        return new Promise(function(resolve, reject) {
                            pending[id] = { resolve: resolve, reject: reject };
                        });
                    });
                    post(JSON.stringify(requests));
                    return Promise.all(resultPromises);
                },
                _onResponse: function(jsonStr) {
                    var msg = JSON.parse(jsonStr);
                    function resolveItem(item) {
                        var p = pending[item.id];
                        if (p) {
                            delete pending[item.id];
                            if (item.error) {
                                p.reject(new Error(item.error.message || 'RPC error'));
                            } else {
                                p.resolve(item.result);
                            }
                        }
                    }
                    if (Array.isArray(msg)) {
                        for (var i = 0; i < msg.length; i++) resolveItem(msg[i]);
                    } else {
                        resolveItem(msg);
                    }
                },
                _createAsyncIterable: function(method, params) {
                    var rpc = window.agWebView.rpc;
                    return {
                        [Symbol.asyncIterator]: function() {
                            var token = null;
                            var buffer = [];
                            var done = false;
                            var initPromise = rpc.invoke(method, params).then(function(r) {
                                token = r.token;
                                if (r.values) { for (var i = 0; i < r.values.length; i++) buffer.push(r.values[i]); }
                                if (r.finished) done = true;
                            });
                            return {
                                next: function() {
                                    return initPromise.then(function() {
                                        if (buffer.length > 0) return { value: buffer.shift(), done: false };
                                        if (done) return { value: undefined, done: true };
                                        return rpc.invoke('$/enumerator/next/' + token).then(function(r) {
                                            if (r.finished) { done = true; return { value: undefined, done: true }; }
                                            if (r.values && r.values.length > 0) return { value: r.values[0], done: false };
                                            return { value: undefined, done: true };
                                        });
                                    });
                                },
                                return: function() {
                                    if (token && !done) {
                                        done = true;
                                        post(JSON.stringify({ jsonrpc: '2.0', method: '$/enumerator/abort', params: { token: token } }));
                                    }
                                    return Promise.resolve({ value: undefined, done: true });
                                }
                            };
                        }
                    };
                }
            };
        })();
        """;

    // ==================== JSON-RPC DTOs ====================

    internal sealed class RpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Params { get; set; }
    }

    internal sealed class RpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Result { get; set; }
    }

    internal sealed class RpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RpcErrorData? Data { get; set; }
    }

    internal sealed class RpcErrorData
    {
        [JsonPropertyName("diagnosticCode")]
        public int DiagnosticCode { get; set; }

        [JsonPropertyName("hint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Hint { get; set; }
    }

    internal sealed class RpcErrorResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("error")]
        public RpcError? Error { get; set; }
    }

    internal sealed class ActiveEnumerator(
        Func<Task<(object? Value, bool Finished)>> moveNext,
        Func<Task> dispose) : IDisposable
    {
        private CancellationTokenSource? _inactivityCts;

        public Func<Task<(object? Value, bool Finished)>> MoveNext { get; } = moveNext;
        public Func<Task> DisposeAsync { get; } = dispose;

        public void StartInactivityTimer(TimeSpan timeout, Func<Task> onTimeout)
        {
            _inactivityCts?.Dispose();
            _inactivityCts = new CancellationTokenSource(timeout);
            _inactivityCts.Token.Register(() => _ = onTimeout(), useSynchronizationContext: false);
        }

        public void ResetInactivityTimer(TimeSpan timeout, Func<Task> onTimeout)
        {
            StartInactivityTimer(timeout, onTimeout);
        }

        public void Dispose()
        {
            _inactivityCts?.Dispose();
            _inactivityCts = null;
        }
    }

    internal sealed class EnumeratorNextResult
    {
        [JsonPropertyName("values")]
        public object?[] Values { get; set; } = [];

        [JsonPropertyName("finished")]
        public bool Finished { get; set; }
    }

    internal sealed class EnumeratorInitResult
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = "";

        [JsonPropertyName("values")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object?[]? Values { get; set; }
    }
}

[JsonSerializable(typeof(WebViewRpcService.RpcRequest))]
[JsonSerializable(typeof(WebViewRpcService.RpcResponse))]
[JsonSerializable(typeof(WebViewRpcService.RpcErrorResponse))]
[JsonSerializable(typeof(WebViewRpcService.RpcError))]
[JsonSerializable(typeof(WebViewRpcService.RpcErrorData))]
[JsonSerializable(typeof(string))]
internal partial class RpcJsonContext : JsonSerializerContext
{
}
