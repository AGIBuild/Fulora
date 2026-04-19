using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for RuntimeBridgeService.
// EventId range: 3300-3399 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class RuntimeBridgeServiceLog
{
    [LoggerMessage(EventId = 3300, Level = LogLevel.Debug,
        Message = "Bridge: removed {Service}")]
    public static partial void LogBridgeServiceRemoved(this ILogger logger, string service);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning,
        Message = "Bridge: failed to async-dispose implementation for {Service}")]
    public static partial void LogAsyncDisposeFailed(this ILogger logger, Exception exception, string service);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Warning,
        Message = "Bridge: failed to dispose implementation for {Service}")]
    public static partial void LogDisposeFailed(this ILogger logger, Exception exception, string service);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Debug,
        Message = "Bridge: failed to clean up JS stub for {Service}")]
    public static partial void LogJsStubCleanupFailed(this ILogger logger, Exception exception, string service);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Debug,
        Message = "Bridge: disposed")]
    public static partial void LogBridgeDisposed(this ILogger logger);

    [LoggerMessage(EventId = 3305, Level = LogLevel.Debug,
        Message = "Bridge: re-injected JS stub for {Service}")]
    public static partial void LogJsStubReInjected(this ILogger logger, string service);
}
