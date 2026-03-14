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
        lock (_callsLock) { _calls.Add(record); }
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        var (paramsJson, _) = DequeuePending(serviceName, methodName);
        var record = new BridgeCallRecord(
            serviceName, methodName, BridgeCallDirection.Export,
            paramsJson, null, exception?.Message ?? "Unknown error", elapsedMs, DateTimeOffset.UtcNow);
        lock (_callsLock) { _calls.Add(record); }
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
        lock (_callsLock) { _calls.Add(record); }
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
        var list = _calls.ToList();
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
        var existing = GetBridgeCalls(service).FirstOrDefault(c => string.Equals(c.MethodName, method, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var match = GetBridgeCalls(service).FirstOrDefault(c => string.Equals(c.MethodName, method, StringComparison.Ordinal));
            if (match != null)
            {
                return match;
            }

            try
            {
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        throw new TimeoutException($"No bridge call to {service}.{method} arrived within the timeout.");
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
    }

    private static string Key(string service, string method) => $"{service}|{method}";

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
