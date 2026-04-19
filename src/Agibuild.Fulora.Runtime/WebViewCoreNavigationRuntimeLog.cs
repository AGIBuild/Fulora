using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreNavigationRuntime.
// EventId range: 2100-2199 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreNavigationRuntimeLog
{
    [LoggerMessage(EventId = 2100, Level = LogLevel.Debug,
        Message = "Stop: canceling active navigation id={NavigationId}")]
    public static partial void LogStopCancelActive(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug,
        Message = "Dispose: faulting active navigation id={NavigationId}")]
    public static partial void LogDisposeFaultActive(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Debug,
        Message = "Event NavigationCompleted: id={NavigationId}, status={Status}, uri={Uri}, error={Error}")]
    public static partial void LogEventNavigationCompleted(this ILogger logger, Guid navigationId, NavigationCompletedStatus status, Uri? uri, string? error);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: correlationId={CorrelationId}, uri={Uri}, isMainFrame={IsMainFrame}")]
    public static partial void LogNativeStarting(this ILogger logger, Guid correlationId, Uri uri, bool isMainFrame);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: disposed, denying")]
    public static partial void LogNativeStartingDisposed(this ILogger logger);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: sub-frame, auto-allow")]
    public static partial void LogNativeStartingSubFrame(this ILogger logger);

    [LoggerMessage(EventId = 2106, Level = LogLevel.Debug,
        Message = "Event NavigationStarted (native): id={NavigationId}, uri={Uri}")]
    public static partial void LogEventNavigationStartedNative(this ILogger logger, Guid navigationId, Uri? uri);

    [LoggerMessage(EventId = 2107, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: canceled by handler, id={NavigationId}")]
    public static partial void LogNativeStartingCanceled(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2108, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: allowed, id={NavigationId}")]
    public static partial void LogNativeStartingAllowed(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2109, Level = LogLevel.Debug,
        Message = "Adapter.NavigationCompleted received: id={NavigationId}, status={Status}, uri={Uri}")]
    public static partial void LogAdapterNavCompletedReceived(this ILogger logger, Guid navigationId, NavigationCompletedStatus status, Uri? uri);

    [LoggerMessage(EventId = 2110, Level = LogLevel.Debug,
        Message = "Adapter.NavigationCompleted: no active navigation, ignoring id={NavigationId}")]
    public static partial void LogAdapterNavCompletedNoActive(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Debug,
        Message = "Adapter.NavigationCompleted: id mismatch (received={Received}, active={Active}), ignoring")]
    public static partial void LogAdapterNavCompletedIdMismatch(this ILogger logger, Guid received, Guid active);

    [LoggerMessage(EventId = 2112, Level = LogLevel.Debug,
        Message = "StartNavigation: superseding active navigation id={NavigationId}")]
    public static partial void LogStartNavigationSuperseding(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2113, Level = LogLevel.Debug,
        Message = "Event NavigationStarted (API): id={NavigationId}, uri={Uri}")]
    public static partial void LogEventNavigationStartedApi(this ILogger logger, Guid navigationId, Uri? uri);

    [LoggerMessage(EventId = 2114, Level = LogLevel.Debug,
        Message = "StartNavigation: canceled by handler, id={NavigationId}")]
    public static partial void LogStartNavigationCanceled(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2115, Level = LogLevel.Debug,
        Message = "StartCommandNavigation: superseding active navigation id={NavigationId}")]
    public static partial void LogStartCommandSuperseding(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2116, Level = LogLevel.Debug,
        Message = "Event NavigationStarted (command): id={NavigationId}, uri={Uri}")]
    public static partial void LogEventNavigationStartedCommand(this ILogger logger, Guid navigationId, Uri? uri);

    [LoggerMessage(EventId = 2117, Level = LogLevel.Debug,
        Message = "StartCommandNavigation: canceled by handler, id={NavigationId}")]
    public static partial void LogStartCommandCanceled(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2118, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: same-URL redirect, id={NavigationId}")]
    public static partial void LogNativeRedirectSameUrl(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2119, Level = LogLevel.Debug,
        Message = "Event NavigationStarted (redirect): id={NavigationId}, uri={Uri}")]
    public static partial void LogEventNavigationStartedRedirect(this ILogger logger, Guid navigationId, Uri? uri);

    [LoggerMessage(EventId = 2120, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: redirect canceled by handler, id={NavigationId}")]
    public static partial void LogNativeRedirectCanceled(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2121, Level = LogLevel.Debug,
        Message = "OnNativeNavigationStarting: superseding active navigation id={NavigationId}")]
    public static partial void LogNativeSuperseding(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2122, Level = LogLevel.Debug,
        Message = "StartNavigation: adapter invocation failed, id={NavigationId}")]
    public static partial void LogStartNavigationAdapterFailed(this ILogger logger, Exception exception, Guid navigationId);
}
