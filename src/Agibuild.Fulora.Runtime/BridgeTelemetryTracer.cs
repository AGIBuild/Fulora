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

    private const string MetricPrefix = "fulora.bridge.";
    private const string EventPrefix = "fulora.bridge.";

    /// <summary>Creates a tracer that reports to the given telemetry provider.</summary>
    /// <param name="provider">The telemetry provider to report to.</param>
    /// <param name="inner">Optional inner tracer to delegate to.</param>
    public BridgeTelemetryTracer(ITelemetryProvider provider, IBridgeTracer? inner = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _inner = inner is NullBridgeTracer ? null : inner;
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _inner?.OnExportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        _provider.TrackMetric(
            MetricPrefix + "export.latency_ms",
            elapsedMs,
            new Dictionary<string, string> { ["service"] = serviceName, ["method"] = methodName });
        _inner?.OnExportCallEnd(serviceName, methodName, elapsedMs, resultType);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
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
        _inner?.OnImportCallStart(serviceName, methodName, paramsJson);
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        _provider.TrackMetric(
            MetricPrefix + "import.latency_ms",
            elapsedMs,
            new Dictionary<string, string> { ["service"] = serviceName, ["method"] = methodName });
        _inner?.OnImportCallEnd(serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _inner?.OnServiceExposed(serviceName, methodCount, isSourceGenerated);
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _inner?.OnServiceRemoved(serviceName);
    }
}
