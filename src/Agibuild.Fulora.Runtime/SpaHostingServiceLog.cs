using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for SpaHostingService.
// EventId range: 2600-2699 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class SpaHostingServiceLog
{
    [LoggerMessage(EventId = 2600, Level = LogLevel.Debug,
        Message = "SPA: dev proxy mode \u2192 {DevServer}")]
    public static partial void LogDevProxyMode(this ILogger logger, string devServer);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Debug,
        Message = "SPA: embedded mode, prefix={Prefix}, assembly={Assembly}")]
    public static partial void LogEmbeddedMode(this ILogger logger, string prefix, string? assembly);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Debug,
        Message = "SPA: external active-asset mode enabled")]
    public static partial void LogExternalAssetsMode(this ILogger logger);

    [LoggerMessage(EventId = 2603, Level = LogLevel.Debug,
        Message = "SPA: fallback \u2192 {Path}")]
    public static partial void LogFallback(this ILogger logger, string path);

    [LoggerMessage(EventId = 2604, Level = LogLevel.Debug,
        Message = "SPA: resource not found '{Resource}', fallback to '{Fallback}'")]
    public static partial void LogResourceNotFoundWithFallback(this ILogger logger, string resource, string fallback);

    [LoggerMessage(EventId = 2605, Level = LogLevel.Debug,
        Message = "SPA: resource not found '{Resource}'")]
    public static partial void LogResourceNotFound(this ILogger logger, string resource);

    [LoggerMessage(EventId = 2606, Level = LogLevel.Debug,
        Message = "SPA: served embedded '{Path}' ({ContentType})")]
    public static partial void LogServedEmbedded(this ILogger logger, string path, string contentType);

    [LoggerMessage(EventId = 2607, Level = LogLevel.Warning,
        Message = "SPA: dev proxy failed for '{Path}'")]
    public static partial void LogDevProxyFailed(this ILogger logger, System.Exception exception, string path);

    [LoggerMessage(EventId = 2608, Level = LogLevel.Debug,
        Message = "SPA: proxied '{Path}' \u2192 {Status} ({ContentType})")]
    public static partial void LogProxied(this ILogger logger, string? path, int status, string? contentType);
}
