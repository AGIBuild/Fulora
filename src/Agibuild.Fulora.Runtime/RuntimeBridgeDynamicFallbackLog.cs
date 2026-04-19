using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for RuntimeBridgeDynamicFallback.
// EventId range: 3500-3599 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class RuntimeBridgeDynamicFallbackLog
{
    [LoggerMessage(EventId = 3500, Level = LogLevel.Debug,
        Message = "Bridge: exposed {Service} with {Count} methods (reflection)")]
    public static partial void LogServiceExposedReflection(this ILogger logger, string service, int count);

    [LoggerMessage(EventId = 3501, Level = LogLevel.Debug,
        Message = "Bridge: created import proxy for {Service}")]
    public static partial void LogImportProxyCreated(this ILogger logger, string service);
}
