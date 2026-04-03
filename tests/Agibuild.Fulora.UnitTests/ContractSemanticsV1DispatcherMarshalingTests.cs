using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Verifies that WebViewCore correctly marshals async/cross-thread calls
/// to the UI thread via the dispatcher for SetCustomUserAgent,
/// TryGetWebViewHandle, and all CookieManager methods.
/// </summary>
public sealed class ContractSemanticsV1DispatcherMarshalingTests
{
    // ==================== SetCustomUserAgent ====================

    [Fact]
    public void SetCustomUserAgent_off_thread_dispatches_to_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithOptions();
        using var core = new WebViewCore(adapter, dispatcher);

        // Call from a background thread.
        var thread = new Thread(() => core.SetCustomUserAgent("BgThread/1.0"))
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        // The adapter should not have been called yet (queued on dispatcher).
        Assert.Equal(0, adapter.SetUserAgentCallCount);

        // Drain the dispatcher queue on the "UI thread".
        dispatcher.RunAll();

        // Now the adapter should have received the call.
        Assert.Equal(1, adapter.SetUserAgentCallCount);
        Assert.Equal("BgThread/1.0", adapter.AppliedUserAgent);
    }

    [Fact]
    public void SetCustomUserAgent_on_ui_thread_executes_directly()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithOptions();
        using var core = new WebViewCore(adapter, dispatcher);

        // Call on the UI thread (same thread that created the dispatcher).
        core.SetCustomUserAgent("UiThread/1.0");

        Assert.Equal(1, adapter.SetUserAgentCallCount);
        Assert.Equal("UiThread/1.0", adapter.AppliedUserAgent);
    }

    // ==================== TryGetWebViewHandle ====================

    [Fact]
    public void TryGetWebViewHandle_off_thread_dispatches_to_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithHandle();
        adapter.HandleToReturn = new TestPlatformHandle(new IntPtr(0x42), "TestHandle");
        using var core = new WebViewCore(adapter, dispatcher);

        INativeHandle? result = null;
        Exception? thrown = null;

        // Call from a background thread — the dispatcher queues work.
        var thread = new Thread(() =>
        {
            try
            {
                result = core.TryGetWebViewHandle();
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        })
        {
            IsBackground = true
        };
        thread.Start();

        // Drain the dispatcher queue on the "UI thread" until the blocking call completes.
        DispatcherTestPump.WaitUntil(dispatcher, () => result is not null || thrown is not null);
        thread.Join();

        Assert.Null(thrown);
        Assert.NotNull(result);
        Assert.Equal("TestHandle", result!.HandleDescriptor);
        Assert.Equal(1, adapter.TryGetHandleCallCount);
        Assert.Equal(dispatcher.UiThreadId, adapter.LastTryGetHandleThreadId);
    }

    [Fact]
    public async Task TryGetWebViewHandleAsync_off_thread_dispatches_to_ui_thread()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithHandle();
        adapter.HandleToReturn = new TestPlatformHandle(new IntPtr(0x55), "AsyncHandle");
        using var core = new WebViewCore(adapter, dispatcher);

        Task<INativeHandle?>? handleTask = null;

        var thread = new Thread(() =>
        {
            handleTask = core.TryGetWebViewHandleAsync();
        })
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        Assert.NotNull(handleTask);
        Assert.False(handleTask!.IsCompleted);

        DispatcherTestPump.WaitUntil(dispatcher, () => handleTask.IsCompleted);

        var result = await handleTask;
        Assert.NotNull(result);
        Assert.Equal("AsyncHandle", result!.HandleDescriptor);
        Assert.Equal(1, adapter.TryGetHandleCallCount);
        Assert.Equal(dispatcher.UiThreadId, adapter.LastTryGetHandleThreadId);
    }

    [Fact]
    public void TryGetWebViewHandle_on_ui_thread_executes_directly()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithHandle();
        adapter.HandleToReturn = new TestPlatformHandle(new IntPtr(0x99), "DirectHandle");
        using var core = new WebViewCore(adapter, dispatcher);

        var result = core.TryGetWebViewHandle();

        Assert.NotNull(result);
        Assert.Equal("DirectHandle", result!.HandleDescriptor);
        Assert.Equal(1, adapter.TryGetHandleCallCount);
        Assert.Equal(dispatcher.UiThreadId, adapter.LastTryGetHandleThreadId);
    }

    [Fact]
    public void TryGetWebViewHandle_returns_null_when_adapter_unsupported()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, dispatcher);

        var result = core.TryGetWebViewHandle();

        Assert.Null(result);
    }

    // ==================== CookieManager dispatching ====================

    [Fact]
    public async Task CookieManager_GetCookiesAsync_off_thread_dispatches()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        using var core = new WebViewCore(adapter, dispatcher);
        var cm = core.TryGetCookieManager()!;

        // Seed a cookie on UI thread.
        await cm.SetCookieAsync(new WebViewCookie("k", "v", ".example.com", "/", null, false, false));

        // Get from background thread.
        Task<IReadOnlyList<WebViewCookie>> bgTask = null!;
        var thread = new Thread(() =>
        {
            bgTask = cm.GetCookiesAsync(new Uri("https://example.com/"));
        })
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        Assert.False(bgTask.IsCompleted);

        // Drain until background task completes.
        DispatcherTestPump.WaitUntil(dispatcher, () => bgTask.IsCompleted);

        var cookies = await bgTask;
        Assert.Single(cookies);
        Assert.Equal("k", cookies[0].Name);
    }

    [Fact]
    public async Task CookieManager_SetCookieAsync_off_thread_dispatches()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        using var core = new WebViewCore(adapter, dispatcher);
        var cm = core.TryGetCookieManager()!;

        Task bgTask = null!;
        var thread = new Thread(() =>
        {
            bgTask = cm.SetCookieAsync(new WebViewCookie("x", "y", ".test.com", "/", null, false, false));
        })
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        Assert.False(bgTask.IsCompleted);

        DispatcherTestPump.WaitUntil(dispatcher, () => bgTask.IsCompleted);
        await bgTask;

        // Verify cookie was stored.
        var cookies = await cm.GetCookiesAsync(new Uri("https://test.com/"));
        Assert.Single(cookies);
        Assert.Equal("x", cookies[0].Name);
    }

    [Fact]
    public async Task CookieManager_DeleteCookieAsync_off_thread_dispatches()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        using var core = new WebViewCore(adapter, dispatcher);
        var cm = core.TryGetCookieManager()!;

        // Seed.
        await cm.SetCookieAsync(new WebViewCookie("del", "val", ".example.com", "/", null, false, false));

        // Delete from background thread.
        Task bgTask = null!;
        var thread = new Thread(() =>
        {
            bgTask = cm.DeleteCookieAsync(new WebViewCookie("del", "val", ".example.com", "/", null, false, false));
        })
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        Assert.False(bgTask.IsCompleted);

        DispatcherTestPump.WaitUntil(dispatcher, () => bgTask.IsCompleted);
        await bgTask;

        // Verify deleted.
        var cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);
    }

    [Fact]
    public async Task CookieManager_ClearAllCookiesAsync_off_thread_dispatches()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        using var core = new WebViewCore(adapter, dispatcher);
        var cm = core.TryGetCookieManager()!;

        // Seed.
        await cm.SetCookieAsync(new WebViewCookie("a", "1", ".example.com", "/", null, false, false));
        await cm.SetCookieAsync(new WebViewCookie("b", "2", ".example.com", "/", null, false, false));

        // Clear from background thread.
        Task bgTask = null!;
        var thread = new Thread(() =>
        {
            bgTask = cm.ClearAllCookiesAsync();
        })
        {
            IsBackground = true
        };
        thread.Start();
        thread.Join();

        Assert.False(bgTask.IsCompleted);

        DispatcherTestPump.WaitUntil(dispatcher, () => bgTask.IsCompleted);
        await bgTask;

        // Verify cleared.
        var cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);
    }

    [Fact]
    public async Task CookieManager_on_ui_thread_executes_directly()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        using var core = new WebViewCore(adapter, dispatcher);
        var cm = core.TryGetCookieManager()!;

        // All calls on UI thread — should execute directly without queueing.
        await cm.SetCookieAsync(new WebViewCookie("direct", "val", ".example.com", "/", null, false, false));

        Assert.Equal(0, dispatcher.QueuedCount);

        var cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Single(cookies);
        Assert.Equal("direct", cookies[0].Name);

        await cm.DeleteCookieAsync(new WebViewCookie("direct", "val", ".example.com", "/", null, false, false));
        cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);

        await cm.SetCookieAsync(new WebViewCookie("c1", "v1", ".example.com", "/", null, false, false));
        await cm.ClearAllCookiesAsync();
        cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);

        Assert.Equal(0, dispatcher.QueuedCount);
    }
}
