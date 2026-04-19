using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for RuntimeBridgeGeneratedPath.
// EventId range: 3400-3499 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class RuntimeBridgeGeneratedPathLog
{
    [LoggerMessage(EventId = 3400, Level = LogLevel.Debug,
        Message = "Bridge: exposed {Service} with {Count} methods (source-generated)")]
    public static partial void LogServiceExposedGenerated(this ILogger logger, string service, int count);
}
