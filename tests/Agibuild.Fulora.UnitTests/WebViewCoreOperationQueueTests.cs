using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewCoreOperationQueueTests
{
    /// <summary>
    /// Dispatcher that always reports UI-thread access and runs work synchronously on the caller's
    /// thread. This bypasses the <c>CheckAccess → InvokeAsync</c> path inside the queue so tests
    /// can assert queue behaviour without also pumping a dispatcher work queue.
    /// </summary>
    private sealed class InlineDispatcher : IWebViewDispatcher
    {
        public bool CheckAccess() => true;
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
        public Task InvokeAsync(Func<Task> func) => func();
        public Task<T> InvokeAsync<T>(Func<Task<T>> func) => func();
    }

    private static WebViewCoreOperationQueue CreateQueue(out WebViewLifecycleStateMachine lifecycle)
    {
        lifecycle = new WebViewLifecycleStateMachine();

        // Transition to Ready so operations are admitted by default.
        lifecycle.TransitionToAttaching();
        lifecycle.TransitionToReady();

        return new WebViewCoreOperationQueue(lifecycle, new InlineDispatcher(), NullLogger.Instance);
    }

    [Fact]
    public void Constructor_rejects_null_lifecycle()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WebViewCoreOperationQueue(null!, new InlineDispatcher(), NullLogger.Instance));
    }

    [Fact]
    public void Constructor_rejects_null_dispatcher()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WebViewCoreOperationQueue(new WebViewLifecycleStateMachine(), null!, NullLogger.Instance));
    }

    [Fact]
    public void Constructor_rejects_null_logger()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WebViewCoreOperationQueue(new WebViewLifecycleStateMachine(), new InlineDispatcher(), null!));
    }

    [Fact]
    public async Task EnqueueAsync_returns_disposed_failure_when_lifecycle_is_disposed_upfront()
    {
        var queue = CreateQueue(out var lifecycle);
        lifecycle.TryTransitionToDisposed();

        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => queue.EnqueueAsync<int>("Op", () => Task.FromResult(1)));

        Assert.True(WebViewOperationFailure.TryGetCategory(ex, out var category));
        Assert.Equal(WebViewOperationFailureCategory.Disposed, category);
    }

    [Fact]
    public async Task EnqueueAsync_returns_not_ready_failure_when_lifecycle_rejects_admission()
    {
        var queue = CreateQueue(out var lifecycle);
        lifecycle.TransitionToDetaching();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync<int>("InvokeScriptAsync", () => Task.FromResult(1)));

        Assert.Contains("not allowed in state 'Detaching'", ex.Message);
        Assert.True(WebViewOperationFailure.TryGetCategory(ex, out var category));
        Assert.Equal(WebViewOperationFailureCategory.NotReady, category);
    }

    [Fact]
    public async Task EnqueueAsync_runs_operation_and_returns_result()
    {
        var queue = CreateQueue(out _);

        var result = await queue.EnqueueAsync("Op", () => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task EnqueueAsync_serializes_operations_by_enqueue_order()
    {
        var queue = CreateQueue(out _);
        var gate1 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate2 = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionOrder = new List<int>();
        var orderLock = new object();

        var first = queue.EnqueueAsync("first", async () =>
        {
            await gate1.Task;
            lock (orderLock) { completionOrder.Add(1); }
            return 1;
        });

        var second = queue.EnqueueAsync("second", async () =>
        {
            await gate2.Task;
            lock (orderLock) { completionOrder.Add(2); }
            return 2;
        });

        gate2.SetResult(0);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(completionOrder);

        gate1.SetResult(0);
        await Task.WhenAll(first, second);

        Assert.Equal([1, 2], completionOrder);
    }

    [Fact]
    public async Task EnqueueAsync_classifies_adapter_failure_exceptions()
    {
        var queue = CreateQueue(out _);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync<int>("Op", () => throw new InvalidOperationException("boom")));

        Assert.True(WebViewOperationFailure.TryGetCategory(ex, out var category));
        Assert.Equal(WebViewOperationFailureCategory.AdapterFailed, category);
        Assert.Equal("Op", ex.Data["operationType"]);
    }

    [Fact]
    public async Task EnqueueAsync_preserves_preclassified_failures()
    {
        var queue = CreateQueue(out _);
        var preClassified = new InvalidOperationException("already classified");
        WebViewOperationFailure.SetCategory(preClassified, WebViewOperationFailureCategory.DispatchFailed);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync<int>("Op", () => throw preClassified));

        Assert.True(WebViewOperationFailure.TryGetCategory(ex, out var category));
        Assert.Equal(WebViewOperationFailureCategory.DispatchFailed, category);
    }

    [Fact]
    public async Task EnqueueAsync_non_generic_overload_returns_null_on_success()
    {
        var queue = CreateQueue(out _);
        var ran = false;

        var result = await queue.EnqueueAsync("Op", () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public async Task EnqueueAsync_propagates_disposal_after_operation_is_already_scheduled()
    {
        var queue = CreateQueue(out var lifecycle);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = queue.EnqueueAsync("first", async () =>
        {
            await gate.Task;
            return 1;
        });

        lifecycle.TryTransitionToDisposed();
        var second = queue.EnqueueAsync<int>("second", () => Task.FromResult(2));

        var ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => second);
        Assert.True(WebViewOperationFailure.TryGetCategory(ex, out var category));
        Assert.Equal(WebViewOperationFailureCategory.Disposed, category);

        gate.SetResult(0);
        Assert.Equal(1, await first);
    }
}
