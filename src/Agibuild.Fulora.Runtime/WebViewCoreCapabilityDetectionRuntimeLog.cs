using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreCapabilityDetectionRuntime.
// EventId range: 2900-2999 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreCapabilityDetectionRuntimeLog
{
    [LoggerMessage(EventId = 2900, Level = LogLevel.Debug,
        Message = "Environment options applied: DevTools={DevTools}, Ephemeral={Ephemeral}, UA={UA}")]
    public static partial void LogEnvironmentOptionsApplied(this ILogger logger, bool devTools, bool ephemeral, string ua);

    [LoggerMessage(EventId = 2901, Level = LogLevel.Debug,
        Message = "Custom schemes registered: {Count}")]
    public static partial void LogCustomSchemesRegistered(this ILogger logger, int count);

    [LoggerMessage(EventId = 2902, Level = LogLevel.Debug,
        Message = "Cookie support: enabled")]
    public static partial void LogCookieSupportEnabled(this ILogger logger);

    [LoggerMessage(EventId = 2903, Level = LogLevel.Debug,
        Message = "Command support: enabled")]
    public static partial void LogCommandSupportEnabled(this ILogger logger);
}
