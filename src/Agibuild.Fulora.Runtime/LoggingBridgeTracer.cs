using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Default tracer that logs all bridge calls via <see cref="ILogger"/>.
/// Attach to <see cref="RuntimeBridgeService"/> for structured call tracing.
/// </summary>
public sealed class LoggingBridgeTracer : IBridgeTracer
{
    private readonly ILogger _logger;
    private readonly IFuloraDiagnosticsSink? _diagnosticsSink;

    /// <inheritdoc />
    public LoggingBridgeTracer(ILogger logger, IFuloraDiagnosticsSink? diagnosticsSink = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnosticsSink = diagnosticsSink;
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.start",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: methodName,
            status: "started",
            attributes: CreatePayloadAttributes(paramsJson)));
        _logger.LogTrace("Bridge → {Service}.{Method} params={Params}",
            serviceName, methodName, Truncate(paramsJson));
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.end",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: methodName,
            status: "success",
            durationMs: elapsedMs,
            attributes: CreateResultAttributes(resultType)));
        _logger.LogTrace("Bridge ← {Service}.{Method} {Elapsed}ms result={ResultType}",
            serviceName, methodName, elapsedMs, resultType ?? "void");
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.export.error",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: methodName,
            status: "error",
            durationMs: elapsedMs,
            errorType: exception.GetType().Name,
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message"] = exception.Message
            }));
        _logger.LogWarning(exception, "Bridge ✗ {Service}.{Method} {Elapsed}ms",
            serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.import.start",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: methodName,
            status: "started",
            attributes: CreatePayloadAttributes(paramsJson)));
        _logger.LogTrace("Bridge ⇒ JS {Service}.{Method} params={Params}",
            serviceName, methodName, Truncate(paramsJson));
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.import.end",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: methodName,
            status: "success",
            durationMs: elapsedMs));
        _logger.LogTrace("Bridge ⇐ JS {Service}.{Method} {Elapsed}ms",
            serviceName, methodName, elapsedMs);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.service.exposed",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: null,
            status: "exposed",
            attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["methodCount"] = methodCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["registrationMode"] = isSourceGenerated ? "source-generated" : "reflection"
            }));
        _logger.LogInformation("Bridge: exposed {Service} ({Count} methods, {Mode})",
            serviceName, methodCount, isSourceGenerated ? "source-generated" : "reflection");
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        _diagnosticsSink?.OnEvent(CreateBridgeEvent(
            eventName: "bridge.service.removed",
            component: nameof(LoggingBridgeTracer),
            serviceName: serviceName,
            methodName: null,
            status: "removed"));
        _logger.LogInformation("Bridge: removed {Service}", serviceName);
    }

    private static string? Truncate(string? s, int maxLen = 200)
        => s is null ? null : s.Length <= maxLen ? s : s[..maxLen] + "…";

    private static IReadOnlyDictionary<string, string> CreatePayloadAttributes(string? paramsJson)
        => string.IsNullOrWhiteSpace(paramsJson)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["params"] = Truncate(paramsJson) ?? string.Empty
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
        string component,
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
            Component = component,
            Service = serviceName,
            Method = methodName,
            DurationMs = durationMs,
            Status = status,
            ErrorType = errorType,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
}
