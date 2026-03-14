using System.Globalization;
using Sentry;
using Sentry.Extensibility;

namespace Agibuild.Fulora.Telemetry;

/// <summary>
/// Sentry implementation of <see cref="ITelemetryProvider"/> that maps events and metrics
/// to breadcrumbs and exceptions to Sentry error events.
/// </summary>
public sealed class SentryTelemetryProvider : ITelemetryProvider
{
    private readonly IHub _hub;
    private readonly SentryFuloraOptions _options;

    /// <summary>Creates a provider using the specified Sentry hub and options.</summary>
    public SentryTelemetryProvider(IHub hub, SentryFuloraOptions options)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Creates a provider using the global <see cref="SentrySdk"/> hub via <see cref="HubAdapter"/>.</summary>
    public SentryTelemetryProvider(SentryFuloraOptions options)
        : this(HubAdapter.Instance, options)
    {
    }

    /// <inheritdoc />
    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        _hub.AddBreadcrumb(
            message: name,
            category: "fulora.event",
            data: properties);
    }

    /// <inheritdoc />
    public void TrackMetric(string name, double value, IDictionary<string, string>? dimensions = null)
    {
        var data = new Dictionary<string, string> { ["value"] = value.ToString("G", CultureInfo.InvariantCulture) };
        if (dimensions != null)
        {
            foreach (var (k, v) in dimensions)
                data[k] = v;
        }

        _hub.AddBreadcrumb(
            message: name,
            category: "fulora.metric",
            data: data);
    }

    /// <inheritdoc />
    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        _hub.CaptureException(exception, scope =>
        {
            if (properties == null) return;
            foreach (var (key, val) in properties)
                scope.SetExtra(key, val);
        });
    }

    /// <inheritdoc />
    public void Flush()
    {
        _hub.FlushAsync(_options.FlushTimeout).GetAwaiter().GetResult();
    }
}
