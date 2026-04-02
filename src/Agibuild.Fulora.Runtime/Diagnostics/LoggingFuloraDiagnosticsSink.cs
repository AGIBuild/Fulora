using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Logger-backed diagnostics sink for unified event envelopes.
/// </summary>
public sealed class LoggingFuloraDiagnosticsSink : IFuloraDiagnosticsSink
{
    private readonly ILogger _logger;

    /// <summary>Create a new logger-backed diagnostics sink.</summary>
    public LoggingFuloraDiagnosticsSink(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void OnEvent(FuloraDiagnosticsEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        var level = string.Equals(diagnosticEvent.Status, "error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(diagnosticEvent.Status, "dropped", StringComparison.OrdinalIgnoreCase)
            ? LogLevel.Warning
            : LogLevel.Information;

        _logger.Log(
            level,
            "Fulora diagnostics {EventName} layer={Layer} component={Component} service={Service} method={Method} status={Status} durationMs={DurationMs} errorType={ErrorType}",
            diagnosticEvent.EventName,
            diagnosticEvent.Layer,
            diagnosticEvent.Component,
            diagnosticEvent.Service,
            diagnosticEvent.Method,
            diagnosticEvent.Status,
            diagnosticEvent.DurationMs,
            diagnosticEvent.ErrorType);
    }
}
