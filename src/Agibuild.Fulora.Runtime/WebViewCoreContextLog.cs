using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreContext.
// EventId range: 3200-3299 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreContextLog
{
    [LoggerMessage(EventId = 3200, Level = LogLevel.Debug,
        Message = "Background operation faulted: {OperationType}")]
    public static partial void LogBackgroundOperationFaulted(this ILogger logger, System.Exception exception, string operationType);
}
