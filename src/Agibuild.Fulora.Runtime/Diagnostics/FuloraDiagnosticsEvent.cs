namespace Agibuild.Fulora;

/// <summary>
/// Unified diagnostics event envelope shared across runtime, bridge, and tooling sinks.
/// </summary>
public sealed record FuloraDiagnosticsEvent
{
    /// <summary>Stable event name.</summary>
    public required string EventName { get; init; }

    /// <summary>Logical layer that produced the event.</summary>
    public required string Layer { get; init; }

    /// <summary>Component that produced the event.</summary>
    public required string Component { get; init; }

    /// <summary>Optional root window identifier.</summary>
    public string? WindowId { get; init; }

    /// <summary>Optional navigation identifier.</summary>
    public string? NavigationId { get; init; }

    /// <summary>Optional channel identifier.</summary>
    public string? ChannelId { get; init; }

    /// <summary>Optional bridge service name.</summary>
    public string? Service { get; init; }

    /// <summary>Optional bridge method name.</summary>
    public string? Method { get; init; }

    /// <summary>Optional call duration in milliseconds.</summary>
    public long? DurationMs { get; init; }

    /// <summary>Optional high-level status token.</summary>
    public string? Status { get; init; }

    /// <summary>Optional error or drop taxonomy token.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Optional correlation identifier.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Optional extra attributes for sinks and tools.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
