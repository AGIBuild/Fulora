using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Owns the serialized operation queue that <see cref="WebViewCore"/> exposes via its public async
/// APIs (<c>NavigateAsync</c>, <c>GoBackAsync</c>, <c>InvokeScriptAsync</c>, ...) and that
/// <see cref="RuntimeCookieManager"/> / <see cref="RuntimeCommandManager"/> consume via the shared
/// <see cref="WebViewCoreContext"/>.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
/// <item>Serialize in-flight operations behind a single tail <see cref="Task"/> so callers observe
/// in-order completion regardless of dispatcher scheduling.</item>
/// <item>Marshal each operation onto the UI thread via <see cref="IWebViewDispatcher"/>, mapping
/// dispatch failures to <see cref="WebViewOperationFailureCategory.DispatchFailed"/>.</item>
/// <item>Classify uncategorised exceptions into <see cref="WebViewOperationFailureCategory"/> so
/// callers can switch on the stable category rather than exception types.</item>
/// </list>
/// Intentionally stateless beyond the queue tail, sequence, and log correlation — does not own any
/// cancellation tokens, timers, or adapter references. The <see cref="WebViewLifecycleStateMachine"/>
/// dictates disposal and admission, so the queue stays a pure serialization primitive.
/// </remarks>
internal sealed class WebViewCoreOperationQueue
{
    private readonly WebViewLifecycleStateMachine _lifecycle;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;
    private readonly object _tailLock = new();
    private Task _tail = Task.CompletedTask;
    private long _sequence;

    public WebViewCoreOperationQueue(
        WebViewLifecycleStateMachine lifecycle,
        IWebViewDispatcher dispatcher,
        ILogger logger)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueues a <see cref="Func{Task}"/> operation, adapting its completion to a
    /// <see cref="Task{TResult}"/> of <see cref="object"/> that is <see langword="null"/> on success.
    /// </summary>
    public Task<object?> EnqueueAsync(string operationType, Func<Task> func)
        => EnqueueAsync<object?>(operationType, async () =>
        {
            await func().ConfigureAwait(false);
            return null;
        });

    /// <summary>
    /// Enqueues a typed async operation. The returned task completes when the operation runs through
    /// the dispatcher, with exceptions classified via <see cref="WebViewOperationFailureCategory"/>.
    /// </summary>
    public Task<T> EnqueueAsync<T>(string operationType, Func<Task<T>> func)
    {
        ArgumentNullException.ThrowIfNull(operationType);
        ArgumentNullException.ThrowIfNull(func);

        if (_lifecycle.IsDisposed)
        {
            return Task.FromException<T>(ClassifyFailure(
                new ObjectDisposedException(nameof(WebViewCore)),
                operationType,
                defaultCategory: WebViewOperationFailureCategory.Disposed));
        }

        if (!_lifecycle.IsOperationAccepted)
        {
            return Task.FromException<T>(ClassifyFailure(
                new InvalidOperationException($"Operation '{operationType}' is not allowed in state '{_lifecycle.CurrentStateName}'."),
                operationType,
                defaultCategory: WebViewOperationFailureCategory.NotReady));
        }

        var operationId = Interlocked.Increment(ref _sequence);
        var enqueueTs = DateTimeOffset.UtcNow;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_tailLock)
        {
            _tail = _tail.ContinueWith(
                _ => RunQueuedOperationAsync(operationId, operationType, enqueueTs, func, tcs),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
        }

        return tcs.Task;
    }

    private async Task RunQueuedOperationAsync<T>(
        long operationId,
        string operationType,
        DateTimeOffset enqueueTs,
        Func<Task<T>> func,
        TaskCompletionSource<T> tcs)
    {
        var startTs = DateTimeOffset.UtcNow;
        var startThread = Environment.CurrentManagedThreadId;
        try
        {
            var result = await InvokeAsyncOnUiThread(func).ConfigureAwait(false);
            var endTs = DateTimeOffset.UtcNow;
            _logger.LogDebug(
                "Operation success: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}",
                operationId, operationType, enqueueTs, startTs, endTs, startThread);
            tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            var classified = ClassifyFailure(ex, operationType, WebViewOperationFailureCategory.AdapterFailed);
            var endTs = DateTimeOffset.UtcNow;
            _logger.LogDebug(
                classified,
                "Operation failed: id={OperationId}, type={OperationType}, enqueueTs={EnqueueTs}, startTs={StartTs}, endTs={EndTs}, threadId={ThreadId}",
                operationId, operationType, enqueueTs, startTs, endTs, startThread);
            tcs.TrySetException(classified);
        }
    }

    private Task<T> InvokeAsyncOnUiThread<T>(Func<Task<T>> func)
    {
        if (_lifecycle.IsDisposed)
        {
            return Task.FromException<T>(ClassifyFailure(
                new ObjectDisposedException(nameof(WebViewCore)),
                operationType: "Dispatch",
                defaultCategory: WebViewOperationFailureCategory.Disposed));
        }

        if (_dispatcher.CheckAccess())
        {
            return func();
        }

        return InvokeWithDispatchFailureMappingAsync(func);
    }

    // Non-async by design: try/return + catch/return makes both code paths return a Task<T>,
    // which eliminates the CS0165 fragility of an `async` version (where the compiler could not
    // prove that `dispatchedTask` was assigned before `await` unless the catch block always threw).
    // A future edit that accidentally removes the throw (e.g. replaces with log + return) now fails
    // to compile instead of leaking an unassigned local.
    private Task<T> InvokeWithDispatchFailureMappingAsync<T>(Func<Task<T>> func)
    {
        try
        {
            return _dispatcher.InvokeAsync(func);
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ClassifyFailure(
                ex,
                operationType: "Dispatch",
                defaultCategory: WebViewOperationFailureCategory.DispatchFailed));
        }
    }

    private static Exception ClassifyFailure(
        Exception exception,
        string operationType,
        WebViewOperationFailureCategory defaultCategory)
    {
        if (WebViewOperationFailure.TryGetCategory(exception, out _))
        {
            return exception;
        }

        var category = exception switch
        {
            ObjectDisposedException => WebViewOperationFailureCategory.Disposed,
            InvalidOperationException invalidOp when
                invalidOp.Message.Contains("not allowed in state", StringComparison.OrdinalIgnoreCase)
                => WebViewOperationFailureCategory.NotReady,
            _ => defaultCategory
        };

        WebViewOperationFailure.SetCategory(exception, category);
        exception.Data["operationType"] = operationType;
        return exception;
    }
}
