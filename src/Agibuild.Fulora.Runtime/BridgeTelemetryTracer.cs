namespace Agibuild.Fulora;

/// <summary>
/// An <see cref="IBridgeTracer"/> that auto-collects bridge call metrics via
/// <see cref="ITelemetryProvider"/>. Tracks latency on call end and errors on call error.
/// Optionally delegates to an inner tracer.
/// </summary>
public sealed class BridgeTelemetryTracer : IBridgeTracer
{
    private readonly ITelemetryProvider _provider;
    private readonly IBridgeTracer? _inner;
    private readonly IFuloraDiagnosticsSink? _diagnosticsSink;

    private const string MetricPrefix = "fulora.bridge.";
    private const string EventPrefix = "fulora.bridge.";

    /// <summary>Creates a tracer that reports to the given telemetry provider.</summary>
    /// <param name="provider">The telemetry provider to report to.</param>
    /// <param name="inner">Optional inner tracer to delegate to.</param>
    /// <param name="diagnosticsSink">Optional unified diagnostics sink for normalized event envelopes.</param>
    public BridgeTelemetryTracer(
        ITelemetryProvider provider,
        IBridgeTracer? inner = null,
        IFuloraDiagnosticsSink? diagnosticsSink = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _inner = inner is NullBridgeTracer ? null : inner;
        _diagnosticsSink = diagnosticsSink;
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.start",
            serviceName: serviceName,
            methodName: methodName,
            status: "started",
            attributes: CreatePayloadAttributes(paramsJson)));
        _inner?.OnExportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.end",
            serviceName: serviceName,
            methodName: methodName,
            status: "success",
            durationMs: elapsedMs,
            attributes: CreateResultAttributes(resultType)));
        _provider.TrackMetric(
            MetricPrefix + "export.latency_ms",
            elapsedMs,
            new Dictionary<string, string> { ["service"] = serviceName, ["method"] = methodName });
        _inner?.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.error",
            serviceName: serviceName,
            methodName: methodName,
            status: "error",
            durationMs: elapsedMs,
            errorType: exception.GetType().Name,
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message"] = exception.Message
            }));
        _provider.TrackMetric(
            MetricPrefix + "export.latency_ms",
            elapsedMs,
            new Dictionary<string, string> { ["service"] = serviceName, ["method"] = methodName });
        _provider.TrackEvent(
            EventPrefix + "export.error",
            new Dictionary<string, string>
            {
                ["service"] = serviceName,
                ["method"] = methodName,
                ["error"] = exception.Message,
            });
        _provider.TrackException(exception, new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method"] = methodName,
        });
        _inner?.OnExportCallError(serviceName, methodName, elapsedMs, exception);
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.import.start",
            serviceName: serviceName,
            methodName: methodName,
            status: "started",
            attributes: CreatePayloadAttributes(paramsJson)));
        _inner?.OnImportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.import.end",
            serviceName: serviceName,
            methodName: methodName,
            status: "success",
            durationMs: elapsedMs));
        _provider.TrackMetric(
            MetricPrefix + "import.latency_ms",
            elapsedMs,
            new Dictionary<string, string> { ["service"] = serviceName, ["method"] = methodName });
        _inner?.OnImportCallEnd(serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.service.exposed",
            serviceName: serviceName,
            methodName: null,
            status: "exposed",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["methodCount"] = methodCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["registrationMode"] = isSourceGenerated ? "source-generated" : "reflection"
            }));
        _inner?.OnServiceExposed(serviceName, methodCount, isSourceGenerated);
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.service.removed",
            serviceName: serviceName,
            methodName: null,
            status: "removed"));
        _inner?.OnServiceRemoved(serviceName);
    }

    private static IReadOnlyDictionary<string, string> CreatePayloadAttributes(string? paramsJson)
        => string.IsNullOrWhiteSpace(paramsJson)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["params"] = paramsJson
            };

    private static IReadOnlyDictionary<string, string> CreateResultAttributes(string? resultType)
        => string.IsNullOrWhiteSpace(resultType)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["resultType"] = resultType
            };

    private static FuloraDiagnosticsEvent CreateBridgeEvent(
        string eventName,
        string serviceName,
        string? methodName,
        string status,
        long? durationMs = null,
        string? errorType = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            EventName = eventName,
            Layer = "bridge",
            Component = nameof(BridgeTelemetryTracer),
            Service = serviceName,
            Method = methodName,
            DurationMs = durationMs,
            Status = status,
            ErrorType = errorType,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
}
