using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agibuild.Fulora.Telemetry;

/// <summary>
/// OpenTelemetry implementation of <see cref="IBridgeTracer"/> that creates Activity spans
/// and metrics for bridge calls (export and import).
/// </summary>
public sealed class OpenTelemetryBridgeTracer : IBridgeTracer
{
    /// <summary>Activity source name for Fulora bridge spans.</summary>
    public static readonly string ActivitySourceName = "Agibuild.Fulora";

    /// <summary>Meter name for Fulora bridge metrics.</summary>
    public static readonly string MeterName = "Agibuild.Fulora";

    /// <summary>Attribute key for service name.</summary>
    public const string ServiceNameKey = "fulora.service_name";

    /// <summary>Attribute key for method name.</summary>
    public const string MethodNameKey = "fulora.method_name";

    /// <summary>Attribute key for call direction (export/import).</summary>
    public const string DirectionKey = "fulora.direction";

    /// <summary>Attribute key for optional params JSON (truncated if large).</summary>
    public const string ParamsJsonKey = "fulora.params_json";

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Meter BridgeMeter = new(MeterName);

    private readonly Counter<long> _callCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _latencyHistogram;

    private readonly ConcurrentDictionary<string, Activity?> _activities = new();

    private const int MaxParamsJsonLength = 1024;

    /// <summary>Creates a bridge tracer that exports spans and metrics via OpenTelemetry.</summary>
    public OpenTelemetryBridgeTracer()
    {
        _callCounter = BridgeMeter.CreateCounter<long>("fulora.bridge.call_count");
        _errorCounter = BridgeMeter.CreateCounter<long>("fulora.bridge.call_errors");
        _latencyHistogram = BridgeMeter.CreateHistogram<double>("fulora.bridge.call_latency_ms");
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var key = Key(serviceName, methodName, "export");
        var tags = new ActivityTagsCollection
        {
            { ServiceNameKey, serviceName },
            { MethodNameKey, methodName },
            { DirectionKey, "export" }
        };
        if (!string.IsNullOrEmpty(paramsJson))
        {
            var truncated = paramsJson.Length > MaxParamsJsonLength
                ? paramsJson[..MaxParamsJsonLength] + "..."
                : paramsJson;
            tags.Add(ParamsJsonKey, truncated);
        }

        var activity = Source.StartActivity(
            $"{serviceName}.{methodName}",
            ActivityKind.Internal,
            parentId: null,
            tags);

        _activities[key] = activity;
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        var key = Key(serviceName, methodName, "export");
        if (_activities.TryRemove(key, out var activity))
        {
            activity?.SetTag("fulora.result_type", resultType ?? "");
            activity?.Dispose();
        }

        RecordMetrics(serviceName, methodName, "export", elapsedMs, status: "ok");
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        var key = Key(serviceName, methodName, "export");
        if (_activities.TryRemove(key, out var activity))
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName ?? exception.GetType().Name },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace ?? "" }
            }));
            activity?.Dispose();
        }

        _errorCounter.Add(1, new KeyValuePair<string, object?>(ServiceNameKey, serviceName),
            new KeyValuePair<string, object?>(MethodNameKey, methodName),
            new KeyValuePair<string, object?>(DirectionKey, "export"));

        RecordMetrics(serviceName, methodName, "export", elapsedMs, status: "error");
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var key = Key(serviceName, methodName, "import");
        var tags = new ActivityTagsCollection
        {
            { ServiceNameKey, serviceName },
            { MethodNameKey, methodName },
            { DirectionKey, "import" }
        };
        if (!string.IsNullOrEmpty(paramsJson))
        {
            var truncated = paramsJson.Length > MaxParamsJsonLength
                ? paramsJson[..MaxParamsJsonLength] + "..."
                : paramsJson;
            tags.Add(ParamsJsonKey, truncated);
        }

        var activity = Source.StartActivity(
            $"{serviceName}.{methodName}",
            ActivityKind.Internal,
            parentId: null,
            tags);

        _activities[key] = activity;
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        var key = Key(serviceName, methodName, "import");
        if (_activities.TryRemove(key, out var activity))
        {
            activity?.Dispose();
        }

        RecordMetrics(serviceName, methodName, "import", elapsedMs, status: "ok");
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        // No-op per spec; optional: could add a metric or event
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        // No-op per spec
    }

    private static string Key(string serviceName, string methodName, string direction) =>
        $"{direction}:{serviceName}.{methodName}";

    private void RecordMetrics(string serviceName, string methodName, string direction, long elapsedMs, string status)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new(ServiceNameKey, serviceName),
            new(MethodNameKey, methodName),
            new(DirectionKey, direction),
            new("fulora.status", status)
        };

        _callCounter.Add(1, tags);
        _latencyHistogram.Record(elapsedMs, tags);
    }
}
