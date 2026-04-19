using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

// Source-generated logger extensions for WebViewCoreOperationQueue.
// EventId range: 3100-3199 (see EventId allocation map in commit log).
[ExcludeFromCodeCoverage]
internal static partial class WebViewCoreOperationQueueLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Debug,
        Message = "Operation success: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}")]
    public static partial void LogOperationSuccess(
        this ILogger logger,
        long operationId,
        string operationType,
        System.DateTimeOffset enqueueTs,
        System.DateTimeOffset startTs,
        System.DateTimeOffset endTs,
        int threadId);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Debug,
        Message = "Operation failed: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}")]
    public static partial void LogOperationFailed(
        this ILogger logger,
        System.Exception exception,
        long operationId,
        string operationType,
        System.DateTimeOffset enqueueTs,
        System.DateTimeOffset startTs,
        System.DateTimeOffset endTs,
        int threadId);
}
