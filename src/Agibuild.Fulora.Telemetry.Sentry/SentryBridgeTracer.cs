using System.Globalization;
using Sentry;
using Sentry.Extensibility;

namespace Agibuild.Fulora.Telemetry;

/// <summary>
/// Sentry implementation of <see cref="IBridgeTracer"/> that records bridge calls as breadcrumbs
/// and captures bridge errors as Sentry events with enriched scope context.
/// </summary>
public sealed class SentryBridgeTracer : IBridgeTracer
{
    private const string Category = "fulora.bridge";

    private readonly IHub _hub;
    private readonly SentryFuloraOptions _options;

    /// <summary>Creates a tracer using the specified Sentry hub and options.</summary>
    public SentryBridgeTracer(IHub hub, SentryFuloraOptions options)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Creates a tracer using the global Sentry hub via <see cref="HubAdapter"/>.</summary>
    public SentryBridgeTracer(SentryFuloraOptions options)
        : this(HubAdapter.Instance, options)
    {
    }

    /// <inheritdoc />
    public void OnExportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var data = BuildCallData(serviceName, methodName, paramsJson);
        _hub.AddBreadcrumb("bridge.export.start", Category, null, data, BreadcrumbLevel.Info);
    }

    /// <inheritdoc />
    public void OnExportCallEnd(string serviceName, string methodName, long elapsedMs, string? resultType)
    {
        var data = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method"] = methodName,
            ["elapsed_ms"] = elapsedMs.ToString(CultureInfo.InvariantCulture),
        };
        if (resultType != null) data["result_type"] = resultType;

        _hub.AddBreadcrumb("bridge.export.end", Category, null, data, BreadcrumbLevel.Info);
    }

    /// <inheritdoc />
    public void OnExportCallError(string serviceName, string methodName, long elapsedMs, Exception exception)
    {
        _hub.CaptureException(exception, scope =>
        {
            scope.SetTag("fulora.service_name", serviceName);
            scope.SetTag("fulora.method_name", methodName);
            scope.SetExtra("fulora.elapsed_ms", elapsedMs);
            scope.SetExtra("fulora.direction", "export");
        });
    }

    /// <inheritdoc />
    public void OnImportCallStart(string serviceName, string methodName, string? paramsJson)
    {
        var data = BuildCallData(serviceName, methodName, paramsJson);
        _hub.AddBreadcrumb("bridge.import.start", Category, null, data, BreadcrumbLevel.Info);
    }

    /// <inheritdoc />
    public void OnImportCallEnd(string serviceName, string methodName, long elapsedMs)
    {
        var data = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method"] = methodName,
            ["elapsed_ms"] = elapsedMs.ToString(CultureInfo.InvariantCulture),
        };
        _hub.AddBreadcrumb("bridge.import.end", Category, null, data, BreadcrumbLevel.Info);
    }

    /// <inheritdoc />
    public void OnServiceExposed(string serviceName, int methodCount, bool isSourceGenerated)
    {
        var data = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method_count"] = methodCount.ToString(CultureInfo.InvariantCulture),
            ["source_generated"] = isSourceGenerated.ToString(),
        };
        _hub.AddBreadcrumb("bridge.service.exposed", Category, null, data, BreadcrumbLevel.Info);
    }

    /// <inheritdoc />
    public void OnServiceRemoved(string serviceName)
    {
        var data = new Dictionary<string, string> { ["service"] = serviceName };
        _hub.AddBreadcrumb("bridge.service.removed", Category, null, data, BreadcrumbLevel.Info);
    }

    private Dictionary<string, string> BuildCallData(string serviceName, string methodName, string? paramsJson)
    {
        var data = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method"] = methodName,
        };

        if (_options.CaptureBridgeParams && !string.IsNullOrEmpty(paramsJson))
        {
            data["params"] = paramsJson.Length > _options.MaxBreadcrumbParamsLength
                ? paramsJson[.._options.MaxBreadcrumbParamsLength] + "..."
                : paramsJson;
        }

        return data;
    }
}
