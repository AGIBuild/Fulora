using System.Collections.Concurrent;
using System.Text.Json;

namespace Agibuild.Fulora.Rpc;

/// <summary>
/// Holds the JS → C# handler dispatch table. Two parallel lookups are kept:
/// <list type="bullet">
///   <item><c>handlers</c> — invoked when no cancellation token is available
///         (notifications, untracked requests, fallback for cancellable
///         handlers when the request id is malformed).</item>
///   <item><c>cancellableHandlers</c> — preferred entry point when the
///         dispatcher has a CTS to bind to a tracked request id.</item>
/// </list>
/// Registering a cancellable handler implicitly registers a non-cancellable
/// shim so legacy callers keep working.
/// </summary>
internal sealed class RpcHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> _handlers = new();
    private readonly ConcurrentDictionary<string, Func<JsonElement?, CancellationToken, Task<object?>>> _cancellableHandlers = new();

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

    public void Unregister(string method)
    {
        ArgumentException.ThrowIfNullOrEmpty(method);
        _handlers.TryRemove(method, out _);
        _cancellableHandlers.TryRemove(method, out _);
    }

    /// <summary>
    /// Registers/overwrites a non-cancellable handler. Used by the enumerator
    /// registry to plug per-token <c>$/enumerator/next/{token}</c> entries
    /// without exposing the inner dictionary.
    /// </summary>
    public void RegisterNonCancellable(string method, Func<JsonElement?, Task<object?>> handler)
    {
        _handlers[method] = handler;
    }

    /// <summary>
    /// Removes only the non-cancellable entry. Used by the enumerator registry
    /// when tearing down a per-token next handler.
    /// </summary>
    public void UnregisterNonCancellable(string method)
    {
        _handlers.TryRemove(method, out _);
    }

    public bool TryGetCancellable(string method, out Func<JsonElement?, CancellationToken, Task<object?>> handler)
        => _cancellableHandlers.TryGetValue(method, out handler!);

    public bool TryGet(string method, out Func<JsonElement?, Task<object?>> handler)
        => _handlers.TryGetValue(method, out handler!);
}
