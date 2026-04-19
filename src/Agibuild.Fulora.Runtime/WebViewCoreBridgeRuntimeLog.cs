using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreBridgeRuntime.
// EventId range: 2200-2299 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreBridgeRuntimeLog
{
    [LoggerMessage(EventId = 2200, Level = LogLevel.Warning,
        Message = "BridgeTracer set after Bridge was already created \u2014 change ignored.")]
    public static partial void LogBridgeTracerSetTooLate(this ILogger logger);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Debug,
        Message = "Bridge: auto-created RuntimeBridgeService")]
    public static partial void LogBridgeAutoCreated(this ILogger logger);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Debug,
        Message = "WebMessageBridge enabled: originCount={Count}, protocol={Protocol}")]
    public static partial void LogWebMessageBridgeEnabled(this ILogger logger, int count, int protocol);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Debug,
        Message = "WebMessageBridge disabled")]
    public static partial void LogWebMessageBridgeDisabled(this ILogger logger);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Debug,
        Message = "Bridge: re-injected JS stubs after navigation")]
    public static partial void LogBridgeReInjected(this ILogger logger);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Debug,
        Message = "Event WebMessageReceived: origin={Origin}, channelId={ChannelId}")]
    public static partial void LogEventWebMessageReceived(this ILogger logger, string? origin, System.Guid channelId);

    [LoggerMessage(EventId = 2206, Level = LogLevel.Debug,
        Message = "WebMessageReceived: bridge not enabled, dropping")]
    public static partial void LogWebMessageDroppedBridgeDisabled(this ILogger logger);

    [LoggerMessage(EventId = 2207, Level = LogLevel.Debug,
        Message = "WebMessageReceived: no policy, dropping")]
    public static partial void LogWebMessageDroppedNoPolicy(this ILogger logger);

    [LoggerMessage(EventId = 2208, Level = LogLevel.Debug,
        Message = "WebMessageReceived: handled as RPC message")]
    public static partial void LogWebMessageHandledAsRpc(this ILogger logger);

    [LoggerMessage(EventId = 2209, Level = LogLevel.Debug,
        Message = "WebMessageReceived: policy allowed, forwarding")]
    public static partial void LogWebMessagePolicyAllowed(this ILogger logger);

    [LoggerMessage(EventId = 2210, Level = LogLevel.Debug,
        Message = "WebMessageReceived: policy denied, reason={Reason}")]
    public static partial void LogWebMessagePolicyDenied(this ILogger logger, WebMessageDropReason reason);
}
