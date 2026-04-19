using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreFeatureRuntime.
// EventId range: 3000-3099 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreFeatureRuntimeLog
{
    [LoggerMessage(EventId = 3000, Level = LogLevel.Debug,
        Message = "Global preload scripts applied: {Count}")]
    public static partial void LogPreloadScriptsApplied(this ILogger logger, int count);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Debug,
        Message = "Async preload support: {Supported}")]
    public static partial void LogAsyncPreloadSupport(this ILogger logger, bool supported);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Debug,
        Message = "Drag-drop support: {Supported}")]
    public static partial void LogDragDropSupport(this ILogger logger, bool supported);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Debug,
        Message = "CustomUserAgent set to: {UA}")]
    public static partial void LogCustomUserAgentSet(this ILogger logger, string ua);
}
