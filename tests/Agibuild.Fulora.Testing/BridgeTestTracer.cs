using System.Collections.Concurrent;
using Agibuild.Fulora;

namespace Agibuild.Fulora.Testing;


/// <summary>
/// Record of a single bridge call for test assertions.
/// </summary>
public sealed record BridgeCallRecord(
    string ServiceName,
    string MethodName,
    BridgeCallDirection Direction,
    string? ParamsJson,
    string? ResultType,
    string? ErrorMessage,
    long ElapsedMs,
    DateTimeOffset Timestamp);

/// <summary>
/// <see cref="IBridgeTracer"/> implementation that records all bridge calls for test assertions.
/// </summary>
public sealed class BridgeTestTracer : IBridgeTracer
{
    private readonly List<BridgeCallRecord> _calls = new();
    private readonly object _callsLock = new();
    private readonly ConcurrentDictionary<string, Queue<PendingCall>> _pendingByKey = new();
    private readonly object _pendingLock = new();

    // Event-driven waiters: one list of TCSs per (service|method) key. WaitForBridgeCallAsync
    // registers a TCS instead of polling so completion is deterministic and immediate, even
    // under heavy CI load where Task.Delay-based polling could miss the deadline.
    private readonly Dictionary<string, List<TaskCompletionSource<BridgeCallRecord>>> _waitersByKey = new();
    private readonly object _waitersLock = new();
    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var key = Key(serviceName, methodName);
        lock (_pendingLock)
        {
            var queue = _pendingByKey.GetOrAdd(key, _ => new Queue<PendingCall>());
            queue.Enqueue(new PendingCall(paramsJson, BridgeCallDirection.Export));
        }
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        var (paramsJson, _) = DequeuePending(serviceName, methodName);
        var record = new BridgeCallRecord(
            serviceName, methodName, BridgeCallDirection.Export,
            paramsJson, resultType, null, elapsedMs, DateTimeOffset.UtcNow);
        AddRecordAndSignal(record);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        var (paramsJson, _) = DequeuePending(serviceName, methodName);
        var record = new BridgeCallRecord(
            serviceName, methodName, BridgeCallDirection.Export,
            paramsJson, null, exception?.Message ?? "Unknown error", elapsedMs, DateTimeOffset.UtcNow);
        AddRecordAndSignal(record);
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var key = Key(serviceName, methodName);
        lock (_pendingLock)
        {
            var queue = _pendingByKey.GetOrAdd(key, _ => new Queue<PendingCall>());
            queue.Enqueue(new PendingCall(paramsJson, BridgeCallDirection.Import));
        }
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        var (paramsJson, _) = DequeuePending(serviceName, methodName);
        var record = new BridgeCallRecord(
            serviceName, methodName, BridgeCallDirection.Import,
            paramsJson, null, null, elapsedMs, DateTimeOffset.UtcNow);
        AddRecordAndSignal(record);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated) { }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName) { }

    /// <summary>
    /// Returns all recorded bridge calls, optionally filtered by service name.
    /// </summary>
    public IReadOnlyList<BridgeCallRecord> GetBridgeCalls(string? serviceFilter = null)
    {
        // Snapshot under _callsLock — readers and writers MUST agree on the same lock or
        // List<T>.Add can reallocate the backing array mid-enumeration and ToList() will
        // throw (intermittent failure observed on CI under load).
        List<BridgeCallRecord> list;
        lock (_callsLock)
        {
            list = _calls.ToList();
        }

        if (serviceFilter is not null)
        {
            list = list.Where(c => string.Equals(c.ServiceName, serviceFilter, StringComparison.Ordinal)).ToList();
        }
        return list;
    }

    /// <summary>
    /// Waits for a bridge call matching the given service and method.
    /// </summary>
    /// <param name="service">Service name to match.</param>
    /// <param name="method">Method name to match.</param>
    /// <param name="timeout">Optional timeout. Defaults to 5 seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The matching <see cref="BridgeCallRecord"/>.</returns>
    /// <exception cref="TimeoutException">Thrown when no matching call arrives within the timeout.</exception>
    public async Task<BridgeCallRecord> WaitForBridgeCallAsync(
        string service,
        string method,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var key = Key(service, method);
        var tcs = new TaskCompletionSource<BridgeCallRecord>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register waiter BEFORE checking existing records to avoid the lost-wakeup race:
        // if a record arrives between the check and the registration, the signaller would
        // miss this waiter. AddRecordAndSignal walks the same lock, guaranteeing ordering.
        lock (_waitersLock)
        {
            // Drain existing first while holding the waiters lock so a concurrent end-call
            // signal cannot interleave between the snapshot and the registration.
            var existing = FindMatchingRecord(service, method);
            if (existing != null)
            {
                return existing;
            }

            if (!_waitersByKey.TryGetValue(key, out var waiters))
            {
                waiters = new List<TaskCompletionSource<BridgeCallRecord>>();
                _waitersByKey[key] = waiters;
            }

            waiters.Add(tcs);
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        using var registration = timeoutCts.Token.Register(static state =>
        {
            var (waiterTcs, cancelToken, externalToken) = ((TaskCompletionSource<BridgeCallRecord>, CancellationToken, CancellationToken))state!;
            if (externalToken.IsCancellationRequested)
            {
                waiterTcs.TrySetCanceled(externalToken);
            }
            else
            {
                waiterTcs.TrySetException(new TimeoutException("No matching bridge call arrived within the timeout."));
            }
        }, (tcs, timeoutCts.Token, ct));

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            // Best-effort cleanup so completed waiters don't accumulate when callers reuse the tracer.
            lock (_waitersLock)
            {
                if (_waitersByKey.TryGetValue(key, out var waiters))
                {
                    waiters.Remove(tcs);
                    if (waiters.Count == 0)
                    {
                        _waitersByKey.Remove(key);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clears all recorded calls and resets internal state.
    /// </summary>
    public void Reset()
    {
        lock (_callsLock)
        {
            _calls.Clear();
        }
        lock (_pendingLock)
        {
            _pendingByKey.Clear();
        }
        lock (_waitersLock)
        {
            _waitersByKey.Clear();
        }
    }

    private static string Key(string service, string method) => $"{service}|{method}";

    private void AddRecordAndSignal(BridgeCallRecord record)
    {
        lock (_callsLock)
        {
            _calls.Add(record);
        }

        // Snapshot waiters for this key under the waiters lock, then complete OUTSIDE the lock
        // to avoid running continuations while holding it (continuations could re-enter the tracer).
        TaskCompletionSource<BridgeCallRecord>[]? toComplete = null;
        var key = Key(record.ServiceName, record.MethodName);
        lock (_waitersLock)
        {
            if (_waitersByKey.TryGetValue(key, out var waiters) && waiters.Count > 0)
            {
                toComplete = waiters.ToArray();
                waiters.Clear();
                _waitersByKey.Remove(key);
            }
        }

        if (toComplete != null)
        {
            foreach (var tcs in toComplete)
            {
                tcs.TrySetResult(record);
            }
        }
    }

    private BridgeCallRecord? FindMatchingRecord(string service, string method)
    {
        lock (_callsLock)
        {
            for (var i = 0; i < _calls.Count; i++)
            {
                var record = _calls[i];
                if (string.Equals(record.ServiceName, service, StringComparison.Ordinal) &&
                    string.Equals(record.MethodName, method, StringComparison.Ordinal))
                {
                    return record;
                }
            }
        }
        return null;
    }

    private (string? ParamsJson, BridgeCallDirection Direction) DequeuePending(string serviceName, string methodName)
    {
        var key = Key(serviceName, methodName);
        lock (_pendingLock)
        {
            if (_pendingByKey.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                var pending = queue.Dequeue();
                if (queue.Count == 0)
                {
                    _pendingByKey.TryRemove(key, out _);
                }
                return (pending.ParamsJson, pending.Direction);
            }
        }
        return (null, BridgeCallDirection.Export);
    }

    private sealed record PendingCall(string? ParamsJson, BridgeCallDirection Direction);
}
