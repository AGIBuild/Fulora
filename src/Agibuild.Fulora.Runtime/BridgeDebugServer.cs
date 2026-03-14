using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Lightweight WebSocket server that streams IBridgeTracer events to external tools (e.g., VS Code extension).
/// Opt-in via BridgeDebugServerOptions.Enabled. Localhost-only.
/// </summary>
public sealed class BridgeDebugServer : IBridgeTracer, IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly ConcurrentBag<WebSocket> _clients = [];
    private readonly IBridgeTracer? _inner;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _registryLock = new();
    private readonly List<(string ServiceName, int MethodCount)> _serviceRegistry = [];
    private Task? _acceptTask;

    /// <summary>
    /// Creates a debug server listening on the specified port.
    /// </summary>
    /// <param name="port">Port to listen on (default 9229).</param>
    /// <param name="inner">Optional inner tracer to delegate events to.</param>
    public BridgeDebugServer(int port = 9229, IBridgeTracer? inner = null)
    {
        _inner = inner is NullBridgeTracer ? null : inner;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>
    /// Starts the WebSocket server and begins accepting connections.
    /// </summary>
    public void Start()
    {
        _listener.Start();
        _acceptTask = AcceptClientsAsync(_cts.Token);
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    _clients.Add(wsContext.WebSocket);
                    // Send service registry on connect
                    List<object> services;
                    lock (_registryLock)
                    {
                        services = _serviceRegistry.Select(s => (object)new { serviceName = s.ServiceName, methodCount = s.MethodCount }).ToList();
                    }
                    await SendAsync(wsContext.WebSocket, new { type = "service-registry", services });
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
        }
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _inner?.OnExportCallStart(serviceName, methodName, paramsJson);
        FireAndForgetBroadcast(new
        {
            type = "call-start",
            serviceName,
            methodName,
            direction = "export",
            paramsJson,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        _inner?.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType);
        FireAndForgetBroadcast(new
        {
            type = "call-end",
            serviceName,
            methodName,
            direction = "export",
            elapsedMs,
            resultType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        _inner?.OnExportCallError(serviceName, methodName, elapsedMs, exception);
        FireAndForgetBroadcast(new
        {
            type = "call-error",
            serviceName,
            methodName,
            elapsedMs,
            error = new { message = exception.Message, stack = exception.StackTrace },
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _inner?.OnImportCallStart(serviceName, methodName, paramsJson);
        FireAndForgetBroadcast(new
        {
            type = "call-start",
            serviceName,
            methodName,
            direction = "import",
            paramsJson,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        _inner?.OnImportCallEnd(serviceName, methodName, elapsedMs);
        FireAndForgetBroadcast(new
        {
            type = "call-end",
            serviceName,
            methodName,
            direction = "import",
            elapsedMs,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _inner?.OnServiceExposed(serviceName, methodCount, isSourceGenerated);
        lock (_registryLock)
        {
            var idx = _serviceRegistry.FindIndex(x => x.ServiceName == serviceName);
            var entry = (serviceName, methodCount);
            if (idx >= 0)
                _serviceRegistry[idx] = entry;
            else
                _serviceRegistry.Add(entry);
        }
        FireAndForgetBroadcast(new { type = "service-exposed", serviceName, methodCount });
        FireAndForgetBroadcastRegistry();
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _inner?.OnServiceRemoved(serviceName);
        lock (_registryLock)
        {
            _serviceRegistry.RemoveAll(x => x.ServiceName == serviceName);
        }
        FireAndForgetBroadcast(new { type = "service-removed", serviceName });
        FireAndForgetBroadcastRegistry();
    }

    private void FireAndForgetBroadcast(object message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await BroadcastAsync(message);
            }
            catch
            {
                // Swallow to avoid crashing the app; debug server is best-effort
            }
        });
    }

    private void FireAndForgetBroadcastRegistry()
    {
        List<object> services;
        lock (_registryLock)
        {
            services = _serviceRegistry.Select(s => (object)new { serviceName = s.ServiceName, methodCount = s.MethodCount }).ToList();
        }
        FireAndForgetBroadcast(new { type = "service-registry", services });
    }

    private async Task BroadcastAsync(object message)
    {
        var json = JsonSerializer.Serialize(message);
        var buffer = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(buffer);

        foreach (var client in _clients)
        {
            if (client.State == WebSocketState.Open)
            {
                try
                {
                    await client.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    // Client disconnected
                }
            }
        }
    }

    private static async Task SendAsync(WebSocket ws, object message)
    {
        var json = JsonSerializer.Serialize(message);
        var buffer = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        foreach (var client in _clients)
        {
            if (client.State == WebSocketState.Open)
            {
                try
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutdown", CancellationToken.None);
                }
                catch { /* ignore */ }
            }
            client.Dispose();
        }
        if (_acceptTask != null)
            await _acceptTask;
        _cts.Dispose();
    }
}
