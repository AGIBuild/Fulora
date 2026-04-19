using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreAdapterEventRuntime.
// EventId range: 2500-2599 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreAdapterEventRuntimeLog
{
    [LoggerMessage(EventId = 2500, Level = LogLevel.Debug,
        Message = "Event NewWindowRequested: uri={Uri}")]
    public static partial void LogEventNewWindowRequested(this ILogger logger, System.Uri? uri);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Debug,
        Message = "Event WebResourceRequested")]
    public static partial void LogEventWebResourceRequested(this ILogger logger);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Debug,
        Message = "Event EnvironmentRequested")]
    public static partial void LogEventEnvironmentRequested(this ILogger logger);

    [LoggerMessage(EventId = 2503, Level = LogLevel.Debug,
        Message = "Event DownloadRequested: uri={Uri}, file={File}")]
    public static partial void LogEventDownloadRequested(this ILogger logger, System.Uri uri, string? file);

    [LoggerMessage(EventId = 2504, Level = LogLevel.Debug,
        Message = "Event PermissionRequested: kind={Kind}, origin={Origin}")]
    public static partial void LogEventPermissionRequested(this ILogger logger, WebViewPermissionKind kind, System.Uri? origin);

    [LoggerMessage(EventId = 2505, Level = LogLevel.Debug,
        Message = "NewWindowRequested: unhandled, navigating in-view to {Uri}")]
    public static partial void LogNewWindowUnhandled(this ILogger logger, System.Uri? uri);
}
