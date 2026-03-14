using System.Globalization;
using System.Text;

namespace Agibuild.Fulora;

/// <summary>
/// Telemetry provider that writes events to stdout (useful for development/debugging).
/// Serves as a reference implementation for custom providers.
/// </summary>
public sealed class ConsoleTelemetryProvider : ITelemetryProvider
{
    private static string FormatProps(IDictionary<string, string>? props)
    {
        if (props is null || props.Count == 0)
            return string.Empty;
        var sb = new StringBuilder();
        foreach (var kv in props)
            sb.Append(CultureInfo.InvariantCulture, $", {kv.Key}=\"{kv.Value}\"");
        return sb.ToString();
    }

    /// <inheritdoc />
    public void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        Console.WriteLine($"[Telemetry.Event] name=\"{name}\"{FormatProps(properties)}");
    }

    /// <inheritdoc />
    public void TrackMetric(string name, double value, IDictionary<string, string>? dimensions = null)
    {
        Console.WriteLine($"[Telemetry.Metric] name=\"{name}\", value={value}{FormatProps(dimensions)}");
    }

    /// <inheritdoc />
    public void TrackException(Exception exception, IDictionary<string, string>? properties = null)
    {
        Console.WriteLine($"[Telemetry.Exception] type=\"{exception.GetType().Name}\", message=\"{exception.Message}\"{FormatProps(properties)}");
    }

    /// <inheritdoc />
    public void Flush()
    {
        Console.Out.Flush();
    }
}
