namespace Agibuild.Fulora;

/// <summary>
/// An <see cref="IBridgeTracer"/> that records all bridge events into a
/// <see cref="BridgeEventCollector"/> for the DevTools debug overlay.
/// Optionally delegates to an inner tracer so that logging/other tracing is preserved.
/// </summary>
public sealed class DevToolsPanelTracer : IBridgeTracer
{
    private readonly BridgeEventCollector _collector;
    private readonly IBridgeTracer? _inner;

    private const int MaxPayloadLength = 4096;

    /// <summary>
    /// Initializes a new instance wrapping the given collector and optional inner tracer.
    /// </summary>
    public DevToolsPanelTracer(BridgeEventCollector collector, IBridgeTracer? inner = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _inner = inner is NullBridgeTracer ? null : inner;
    }

    /// <summary>The underlying event collector.</summary>
    public IBridgeEventCollector Collector => _collector;

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Export,
            Phase = BridgeCallPhase.Start,
            ServiceName = serviceName,
            MethodName = methodName,
            ParamsJson = Truncate(paramsJson),
            Truncated = paramsJson is not null && paramsJson.Length > MaxPayloadLength,
        });
        _inner?.OnExportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Export,
            Phase = BridgeCallPhase.End,
            ServiceName = serviceName,
            MethodName = methodName,
            ElapsedMs = elapsedMs,
            ResultJson = resultType,
        });
        _inner?.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception error)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Export,
            Phase = BridgeCallPhase.Error,
            ServiceName = serviceName,
            MethodName = methodName,
            ElapsedMs = elapsedMs,
            ErrorMessage = error.Message,
        });
        _inner?.OnExportCallError(serviceName, methodName, elapsedMs, error);
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Import,
            Phase = BridgeCallPhase.Start,
            ServiceName = serviceName,
            MethodName = methodName,
            ParamsJson = Truncate(paramsJson),
            Truncated = paramsJson is not null && paramsJson.Length > MaxPayloadLength,
        });
        _inner?.OnImportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Import,
            Phase = BridgeCallPhase.End,
            ServiceName = serviceName,
            MethodName = methodName,
            ElapsedMs = elapsedMs,
        });
        _inner?.OnImportCallEnd(serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Lifecycle,
            Phase = BridgeCallPhase.ServiceExposed,
            ServiceName = serviceName,
            ResultJson = $"{methodCount} methods ({(isSourceGenerated ? "generated" : "reflection")})",
        });
        _inner?.OnServiceExposed(serviceName, methodCount, isSourceGenerated);
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _collector.Add(new BridgeDevToolsEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            Direction = BridgeCallDirection.Lifecycle,
            Phase = BridgeCallPhase.ServiceRemoved,
            ServiceName = serviceName,
        });
        _inner?.OnServiceRemoved(serviceName);
    }

    private static string? Truncate(string? s) =>
        s is null ? null : s.Length <= MaxPayloadLength ? s : s[..MaxPayloadLength] + "…";
}
