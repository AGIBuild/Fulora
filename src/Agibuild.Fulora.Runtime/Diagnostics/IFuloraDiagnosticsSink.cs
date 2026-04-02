namespace Agibuild.Fulora;

/// <summary>
/// Sink for unified Fulora diagnostics events.
/// </summary>
public interface IFuloraDiagnosticsSink
{
    /// <summary>Consumes a diagnostics event.</summary>
    void OnEvent(FuloraDiagnosticsEvent diagnosticEvent);
}
