using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreSpaHostingRuntime.
// EventId range: 2300-2399 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreSpaHostingRuntimeLog
{
    [LoggerMessage(EventId = 2300, Level = LogLevel.Debug,
        Message = "SPA hosting enabled: scheme={Scheme}, devServer={DevServer}")]
    public static partial void LogSpaHostingEnabled(this ILogger logger, string scheme, string devServer);
}
