namespace Agibuild.Fulora;

/// <summary>
/// An <see cref="IBridgeTracer"/> that holds multiple tracers and forwards each callback to all.
/// Exceptions from one tracer are caught and do not prevent other tracers from receiving events.
/// <see cref="NullBridgeTracer"/> instances are filtered out and not included.
/// </summary>
public sealed class CompositeBridgeTracer : IBridgeTracer
{
    private readonly IBridgeTracer[] _tracers;

    /// <summary>
    /// Creates a composite tracer that forwards to the given tracers.
    /// </summary>
    /// <param name="tracers">Tracers to forward to. <see cref="NullBridgeTracer"/> instances are excluded.</param>
    public CompositeBridgeTracer(params IBridgeTracer[] tracers)
    {
        _tracers = tracers.Where(t => t is not NullBridgeTracer).ToArray();
    }

    /// <summary>
    /// Creates a composite tracer that forwards to the given tracers.
    /// </summary>
    /// <param name="tracers">Tracers to forward to. <see cref="NullBridgeTracer"/> instances are excluded.</param>
    public CompositeBridgeTracer(IEnumerable<IBridgeTracer> tracers)
        : this(tracers.ToArray())
    {
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        foreach (var t in _tracers)
        {
            try { t.OnExportCallStart(serviceName, methodName, paramsJson); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        foreach (var t in _tracers)
        {
            try { t.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        foreach (var t in _tracers)
        {
            try { t.OnExportCallError(serviceName, methodName, elapsedMs, exception); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        foreach (var t in _tracers)
        {
            try { t.OnImportCallStart(serviceName, methodName, paramsJson); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        foreach (var t in _tracers)
        {
            try { t.OnImportCallEnd(serviceName, methodName, elapsedMs); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        foreach (var t in _tracers)
        {
            try { t.OnServiceExposed(serviceName, methodCount, isSourceGenerated); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        foreach (var t in _tracers)
        {
            try { t.OnServiceRemoved(serviceName); }
            catch { /* isolate per-tracer exceptions */ }
        }
    }
}
