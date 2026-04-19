using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for LoggingBridgeTracer.
// EventId range: 2700-2799 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class LoggingBridgeTracerLog
{
    [LoggerMessage(EventId = 2700, Level = LogLevel.Trace,
        Message = "Bridge \u2192 {Service}.{Method} params={Params}")]
    public static partial void LogExportStart(this ILogger logger, string service, string method, string? @params);

    [LoggerMessage(EventId = 2701, Level = LogLevel.Trace,
        Message = "Bridge \u2190 {Service}.{Method} {Elapsed}ms result={ResultType}")]
    public static partial void LogExportEnd(this ILogger logger, string service, string method, long elapsed, string resultType);

    [LoggerMessage(EventId = 2702, Level = LogLevel.Warning,
        Message = "Bridge \u2717 {Service}.{Method} {Elapsed}ms")]
    public static partial void LogExportError(this ILogger logger, System.Exception exception, string service, string method, long elapsed);

    [LoggerMessage(EventId = 2703, Level = LogLevel.Trace,
        Message = "Bridge \u21d2 JS {Service}.{Method} params={Params}")]
    public static partial void LogImportStart(this ILogger logger, string service, string method, string? @params);

    [LoggerMessage(EventId = 2704, Level = LogLevel.Trace,
        Message = "Bridge \u21d0 JS {Service}.{Method} {Elapsed}ms")]
    public static partial void LogImportEnd(this ILogger logger, string service, string method, long elapsed);

    [LoggerMessage(EventId = 2705, Level = LogLevel.Information,
        Message = "Bridge: exposed {Service} ({Count} methods, {Mode})")]
    public static partial void LogServiceExposed(this ILogger logger, string service, int count, string mode);

    [LoggerMessage(EventId = 2706, Level = LogLevel.Information,
        Message = "Bridge: removed {Service}")]
    public static partial void LogServiceRemoved(this ILogger logger, string service);
}
