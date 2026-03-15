using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Wraps an <see cref="IWebViewRpcService"/> to add <see cref="IBridgeTracer"/> hooks
/// to every handler registered through it.
/// </summary>
internal sealed class TracingRpcWrapper : IWebViewRpcService
{
    private readonly IWebViewRpcService _inner;
    private readonly IBridgeTracer _tracer;
    private readonly string _serviceName;

    public TracingRpcWrapper(IWebViewRpcService inner, IBridgeTracer tracer, string serviceName)
    {
        _inner = inner;
        _tracer = tracer;
        _serviceName = serviceName;
    }

    public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
    {
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithTracing(handler, _tracer, _serviceName, methodName));
    }

    public void Handle(string method, Func<JsonElement?, object?> handler)
    {
        Func<JsonElement?, Task<object?>> asyncHandler = args => Task.FromResult(handler(args));
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithTracing(asyncHandler, _tracer, _serviceName, methodName));
    }

    public void Handle(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
    {
        Func<JsonElement?, Task<object?>> asyncHandler = args => handler(args, CancellationToken.None);
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithTracing(asyncHandler, _tracer, _serviceName, methodName));
    }

    public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose)
        => _inner.RegisterEnumerator(token, moveNext, dispose);

    public void UnregisterHandler(string method) => _inner.UnregisterHandler(method);

    public async Task<JsonElement> InvokeAsync(string method, object? args = null)
    {
        TraceImportStart(method, args);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _inner.InvokeAsync(method, args).ConfigureAwait(false);
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<T?> InvokeAsync<T>(string method, object? args = null)
    {
        TraceImportStart(method, args);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _inner.InvokeAsync<T>(method, args).ConfigureAwait(false);
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken ct)
    {
        TraceImportStart(method, args);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _inner.InvokeAsync(method, args, ct).ConfigureAwait(false);
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken ct)
    {
        TraceImportStart(method, args);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await _inner.InvokeAsync<T>(method, args, ct).ConfigureAwait(false);
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            TraceImportEnd(method, sw.ElapsedMilliseconds);
            throw;
        }
    }

    public Task NotifyAsync(string method, object? args = null) => _inner.NotifyAsync(method, args);

    private void TraceImportStart(string method, object? args)
    {
        var (svc, meth) = RpcMethodHelpers.SplitRpcMethodAsMethod(method);
        _tracer.OnImportCallStart(svc, meth, args is not null ? JsonSerializer.Serialize(args) : null);
    }

    private void TraceImportEnd(string method, long elapsedMs)
    {
        var (svc, meth) = RpcMethodHelpers.SplitRpcMethodAsMethod(method);
        _tracer.OnImportCallEnd(svc, meth, elapsedMs);
    }
}
