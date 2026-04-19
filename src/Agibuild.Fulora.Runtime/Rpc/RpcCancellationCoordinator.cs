using System.Collections.Concurrent;

namespace Agibuild.Fulora.Rpc;

/// <summary>
/// Three-state coordinator for in-flight JS → C# request cancellations.
/// <para>
/// JS may send <c>$/cancelRequest</c> at any time relative to the C# handler
/// dispatch — strictly before the CTS is registered, after it is unregistered,
/// or in between. The three dictionaries together encode the lifecycle:
/// </para>
/// <list type="bullet">
///   <item><c>activeRequestIds</c> — request id is being tracked (between
///         <see cref="MarkActive"/> and <see cref="ClearActive"/>).</item>
///   <item><c>activeCancellations</c> — handler has registered its CTS via
///         <see cref="Register"/> and may be cancelled directly.</item>
///   <item><c>pendingCancellations</c> — cancel arrived for a tracked request
///         whose CTS has not been registered yet; replayed by
///         <see cref="Register"/>.</item>
/// </list>
/// Callers never reach into the dictionaries directly — they go through
/// <see cref="MarkActive"/>/<see cref="ClearActive"/>/<see cref="Register"/>/
/// <see cref="Unregister"/>/<see cref="HandleCancelRequest"/> and the
/// invariants are owned here.
/// </summary>
internal sealed class RpcCancellationCoordinator
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCancellations = new();
    private readonly ConcurrentDictionary<string, byte> _activeRequestIds = new();
    private readonly ConcurrentDictionary<string, byte> _pendingCancellations = new();

    /// <summary>
    /// Marks <paramref name="requestId"/> as in-flight. Must be called before
    /// any chance of dispatching the request so <see cref="HandleCancelRequest"/>
    /// can defer cancellations that race ahead of CTS registration.
    /// </summary>
    public void MarkActive(string requestId)
    {
        _activeRequestIds[requestId] = 0;
    }

    /// <summary>
    /// Clears the in-flight marker and any deferred cancellation for
    /// <paramref name="requestId"/>. Called after the handler completes.
    /// </summary>
    public void ClearActive(string requestId)
    {
        _activeRequestIds.TryRemove(requestId, out _);
        _pendingCancellations.TryRemove(requestId, out _);
    }

    /// <summary>
    /// Binds the supplied <paramref name="cts"/> to <paramref name="requestId"/>.
    /// If a cancel notification has already been received it is replayed
    /// immediately against the new CTS.
    /// </summary>
    public void Register(string requestId, CancellationTokenSource cts)
    {
        _activeCancellations[requestId] = cts;
        if (_pendingCancellations.TryRemove(requestId, out _))
        {
            cts.Cancel();
        }
    }

    /// <summary>
    /// Removes the CTS binding for <paramref name="requestId"/> and disposes
    /// the source. Safe to call even if <see cref="Register"/> was never
    /// invoked (no-op).
    /// </summary>
    public void Unregister(string requestId)
    {
        _pendingCancellations.TryRemove(requestId, out _);
        if (_activeCancellations.TryRemove(requestId, out var cts))
        {
            cts.Dispose();
        }
    }

    /// <summary>
    /// Handles a JS-side <c>$/cancelRequest</c> targeting
    /// <paramref name="requestId"/>. If the CTS is already registered it is
    /// cancelled inline; if the request is in-flight but not yet registered
    /// the cancellation is buffered for the upcoming <see cref="Register"/>;
    /// orphan cancellations (for unknown ids) are dropped silently to mirror
    /// JSON-RPC semantics.
    /// </summary>
    public void HandleCancelRequest(string requestId)
    {
        if (_activeCancellations.TryGetValue(requestId, out var cts))
        {
            cts.Cancel();
            return;
        }

        if (_activeRequestIds.ContainsKey(requestId))
        {
            _pendingCancellations[requestId] = 0;
        }
    }
}
