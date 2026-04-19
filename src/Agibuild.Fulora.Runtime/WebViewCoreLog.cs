using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCore (the orchestrator hot path).
// EventId range: 2000-2099 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug,
        Message = "WebViewCore created: channelId={ChannelId}, adapter={AdapterType}")]
    public static partial void LogCreated(this ILogger logger, Guid channelId, string? adapterType);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug,
        Message = "Adapter initialized")]
    public static partial void LogAdapterInitialized(this ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug,
        Message = "Attach: parentHandle.HandleDescriptor={Descriptor}")]
    public static partial void LogAttachBegin(this ILogger logger, string descriptor);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug,
        Message = "Attach: completed")]
    public static partial void LogAttachCompleted(this ILogger logger);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug,
        Message = "AdapterCreated: raising with handle={HasHandle}")]
    public static partial void LogAdapterCreatedRaising(this ILogger logger, bool hasHandle);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Debug,
        Message = "Detach: begin")]
    public static partial void LogDetachBegin(this ILogger logger);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Debug,
        Message = "Detach: completed")]
    public static partial void LogDetachCompleted(this ILogger logger);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Debug,
        Message = "Source set: {Uri}")]
    public static partial void LogSourceSet(this ILogger logger, Uri? uri);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Debug,
        Message = "Dispose: begin")]
    public static partial void LogDisposeBegin(this ILogger logger);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Debug,
        Message = "Dispose: completed")]
    public static partial void LogDisposeCompleted(this ILogger logger);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug,
        Message = "NavigateAsync: {Uri}")]
    public static partial void LogNavigateAsync(this ILogger logger, Uri uri);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Debug,
        Message = "NavigateToStringAsync: html length={Length}, baseUrl={BaseUrl}")]
    public static partial void LogNavigateToString(this ILogger logger, int length, System.Uri? baseUrl);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Debug,
        Message = "InvokeScriptAsync: script length={Length}")]
    public static partial void LogInvokeScript(this ILogger logger, int length);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Debug,
        Message = "InvokeScriptAsync: result length={Length}")]
    public static partial void LogInvokeScriptResult(this ILogger logger, int length);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Debug,
        Message = "InvokeScriptAsync: failed")]
    public static partial void LogInvokeScriptFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Debug,
        Message = "GoBack: no history, skipped")]
    public static partial void LogGoBackNoHistory(this ILogger logger);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Debug,
        Message = "GoBack: canceled by NavigationStarted handler")]
    public static partial void LogGoBackCanceled(this ILogger logger);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Debug,
        Message = "GoBack: adapter rejected, id={NavigationId}")]
    public static partial void LogGoBackRejected(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Debug,
        Message = "GoBack: started, id={NavigationId}")]
    public static partial void LogGoBackStarted(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2019, Level = LogLevel.Debug,
        Message = "GoForward: no forward history, skipped")]
    public static partial void LogGoForwardNoHistory(this ILogger logger);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Debug,
        Message = "GoForward: canceled by NavigationStarted handler")]
    public static partial void LogGoForwardCanceled(this ILogger logger);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Debug,
        Message = "GoForward: adapter rejected, id={NavigationId}")]
    public static partial void LogGoForwardRejected(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Debug,
        Message = "GoForward: started, id={NavigationId}")]
    public static partial void LogGoForwardStarted(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Debug,
        Message = "Refresh: canceled by NavigationStarted handler")]
    public static partial void LogRefreshCanceled(this ILogger logger);

    [LoggerMessage(EventId = 2024, Level = LogLevel.Debug,
        Message = "Refresh: adapter rejected, id={NavigationId}")]
    public static partial void LogRefreshRejected(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2025, Level = LogLevel.Debug,
        Message = "Refresh: started, id={NavigationId}")]
    public static partial void LogRefreshStarted(this ILogger logger, Guid navigationId);

    [LoggerMessage(EventId = 2026, Level = LogLevel.Debug,
        Message = "Stop: no active navigation")]
    public static partial void LogStopNoActive(this ILogger logger);

    [LoggerMessage(EventId = 2027, Level = LogLevel.Debug,
        Message = "AdapterDestroyed: raising")]
    public static partial void LogAdapterDestroyedRaising(this ILogger logger);
}
