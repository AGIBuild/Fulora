using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// Wraps an <see cref="IWebViewRpcService"/> to apply a middleware pipeline to every handler registered through it.
/// </summary>
internal sealed class MiddlewareRpcWrapper : IWebViewRpcService
{
    private readonly IWebViewRpcService _inner;
    private readonly List<IBridgeMiddleware> _middlewares;
    private readonly string _serviceName;

    public MiddlewareRpcWrapper(IWebViewRpcService inner, List<IBridgeMiddleware> middlewares, string serviceName)
    {
        _inner = inner;
        _middlewares = middlewares;
        _serviceName = serviceName;
    }

    public void Handle(string method, Func<JsonElement?, Task<object?>> handler)
    {
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithMiddleware(handler, _middlewares, _serviceName, methodName));
    }

    public void Handle(string method, Func<JsonElement?, object?> handler)
    {
        Func<JsonElement?, Task<object?>> asyncHandler = args => Task.FromResult(handler(args));
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithMiddleware(asyncHandler, _middlewares, _serviceName, methodName));
    }

    public void Handle(string method, Func<JsonElement?, CancellationToken, Task<object?>> handler)
    {
        Func<JsonElement?, Task<object?>> asyncHandler = args => handler(args, CancellationToken.None);
        var methodName = RpcMethodHelpers.ExtractMethodName(method);
        _inner.Handle(method, RuntimeBridgeService.WrapWithMiddleware(asyncHandler, _middlewares, _serviceName, methodName));
    }

    public void RegisterEnumerator(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose)
        => _inner.RegisterEnumerator(token, moveNext, dispose);

    public void UnregisterHandler(string method) => _inner.UnregisterHandler(method);
    public Task<JsonElement> InvokeAsync(string method, object? args = null) => _inner.InvokeAsync(method, args);
    public Task<T?> InvokeAsync<T>(string method, object? args = null) => _inner.InvokeAsync<T>(method, args);
    public Task<JsonElement> InvokeAsync(string method, object? args, CancellationToken ct) => _inner.InvokeAsync(method, args, ct);
    public Task<T?> InvokeAsync<T>(string method, object? args, CancellationToken ct) => _inner.InvokeAsync<T>(method, args, ct);
    public Task NotifyAsync(string method, object? args = null) => _inner.NotifyAsync(method, args);
}
