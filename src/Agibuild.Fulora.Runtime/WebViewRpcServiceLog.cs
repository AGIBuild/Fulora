using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewRpcService.
// EventId range: 2400-2499 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewRpcServiceLog
{
    [LoggerMessage(EventId = 2400, Level = LogLevel.Debug,
        Message = "RPC: failed to dispose enumerator {Token}")]
    public static partial void LogEnumeratorDisposeFailed(this ILogger logger, System.Exception exception, string token);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Debug,
        Message = "RPC: failed to parse message")]
    public static partial void LogParseMessageFailed(this ILogger logger, System.Exception exception);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Debug,
        Message = "RPC: handler for '{Method}' was cancelled")]
    public static partial void LogHandlerCancelled(this ILogger logger, string method);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Debug,
        Message = "RPC: handler for '{Method}' threw RPC error {Code}")]
    public static partial void LogHandlerRpcError(this ILogger logger, System.Exception exception, string method, int code);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Debug,
        Message = "RPC: handler for '{Method}' threw FuloraException [{ErrorCode}]")]
    public static partial void LogHandlerFuloraException(this ILogger logger, System.Exception exception, string method, string errorCode);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Debug,
        Message = "RPC: handler for '{Method}' failed to deserialize")]
    public static partial void LogHandlerDeserializeFailed(this ILogger logger, System.Exception exception, string method);

    [LoggerMessage(EventId = 2406, Level = LogLevel.Debug,
        Message = "RPC: handler for '{Method}' threw")]
    public static partial void LogHandlerThrew(this ILogger logger, System.Exception exception, string method);
}
