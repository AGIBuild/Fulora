using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for RuntimeCookieManager.
// EventId range: 2800-2899 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class RuntimeCookieManagerLog
{
    [LoggerMessage(EventId = 2800, Level = LogLevel.Debug,
        Message = "CookieManager.GetCookiesAsync: {Uri}")]
    public static partial void LogGetCookies(this ILogger logger, System.Uri uri);

    [LoggerMessage(EventId = 2801, Level = LogLevel.Debug,
        Message = "CookieManager.SetCookieAsync: {Name}@{Domain}")]
    public static partial void LogSetCookie(this ILogger logger, string name, string domain);

    [LoggerMessage(EventId = 2802, Level = LogLevel.Debug,
        Message = "CookieManager.DeleteCookieAsync: {Name}@{Domain}")]
    public static partial void LogDeleteCookie(this ILogger logger, string name, string domain);

    [LoggerMessage(EventId = 2803, Level = LogLevel.Debug,
        Message = "CookieManager.ClearAllCookiesAsync")]
    public static partial void LogClearAllCookies(this ILogger logger);
}
