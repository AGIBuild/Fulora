namespace Agibuild.Fulora;

/// <summary>
/// In-memory diagnostics sink used by tests and interactive tooling.
/// </summary>
public sealed class MemoryFuloraDiagnosticsSink : IFuloraDiagnosticsSink
{
    private readonly object _lock = new();
    private readonly List<FuloraDiagnosticsEvent> _events = [];

    /// <summary>Recorded diagnostics events in insertion order.</summary>
    public IReadOnlyList<FuloraDiagnosticsEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void OnEvent(FuloraDiagnosticsEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        lock (_lock)
        {
            _events.Add(diagnosticEvent);
        }
    }

    /// <summary>Clears recorded events.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }
}
