using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agibuild.Fulora.Telemetry;

/// <summary>
/// OpenTelemetry implementation of <see cref="ITelemetryProvider"/> that maps events,
/// metrics, and exceptions to OTLP-compatible signals.
/// </summary>
public sealed class OpenTelemetryTelemetryProvider : ITelemetryProvider
{
    private static readonly ActivitySource Source = new(OpenTelemetryBridgeTracer.ActivitySourceName);
    private static readonly Meter Meter = new(OpenTelemetryBridgeTracer.MeterName);

    private readonly ConcurrentDictionary<string, Histogram<double>> _metricHistograms = new();

    /// <summary>Creates a telemetry provider that maps events and metrics to OTLP-compatible signals.</summary>
    public OpenTelemetryTelemetryProvider()
    {
    }

    /// <inheritdoc />
    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        try
        {
            if (properties != null)
            {
                foreach (var (key, value) in properties)
                {
                    activity?.SetTag(key, value);
                }
            }
        }
        finally
        {
            activity?.Dispose();
        }
    }

    /// <inheritdoc />
    public void TrackMetric(string name, double value, IDictionary<string, string>? dimensions = null)
    {
        var instrumentName = SanitizeMetricName(name);
        var hist = _metricHistograms.GetOrAdd(instrumentName, n => Meter.CreateHistogram<double>(n));

        if (dimensions is { Count: > 0 })
        {
            var tags = new KeyValuePair<string, object?>[dimensions.Count];
            var i = 0;
            foreach (var (k, v) in dimensions)
            {
                tags[i++] = new KeyValuePair<string, object?>(k, v);
            }
            hist.Record(value, tags);
        }
        else
        {
            hist.Record(value);
        }
    }

    /// <inheritdoc />
    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        var activity = Activity.Current ?? Source.StartActivity("exception", ActivityKind.Internal);
        try
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName ?? exception.GetType().Name },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace ?? "" }
            }));

            if (properties != null)
            {
                foreach (var (key, value) in properties)
                {
                    activity?.SetTag(key, value);
                }
            }
        }
        finally
        {
            if (Activity.Current != activity)
            {
                activity?.Dispose();
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Best-effort: when using OpenTelemetry.Api only (no SDK), there is no TracerProvider to flush.
    /// Completes without throwing when no SDK is configured.
    /// </remarks>
    public void Flush()
    {
        // OpenTelemetry.Api does not expose ForceFlush; that requires the SDK (OpenTelemetry package).
        // No-op when using API-only. Completes without throwing per spec.
    }

    private static string SanitizeMetricName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "fulora.telemetry.unknown";
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }
        var result = new string(sanitized);
        return string.IsNullOrEmpty(result) ? "fulora.telemetry.unknown" : result;
    }
}
