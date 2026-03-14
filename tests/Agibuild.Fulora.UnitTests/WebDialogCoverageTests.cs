using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class CoverageGapTests
{
    [Fact]
    public void WebDialog_event_unsubscribe_paths()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        var dialog = new WebDialog(host, adapter, _dispatcher);

        // Subscribe and unsubscribe each event to cover remove accessors.
        EventHandler<NavigationStartingEventArgs> navStarted = (_, _) => { };
        EventHandler<NavigationCompletedEventArgs> navCompleted = (_, _) => { };
        EventHandler<NewWindowRequestedEventArgs> newWindow = (_, _) => { };
        EventHandler<WebMessageReceivedEventArgs> webMsg = (_, _) => { };
        EventHandler<WebResourceRequestedEventArgs> webRes = (_, _) => { };
        EventHandler<EnvironmentRequestedEventArgs> envReq = (_, _) => { };

        dialog.NavigationStarted += navStarted;
        dialog.NavigationStarted -= navStarted;

        dialog.NavigationCompleted += navCompleted;
        dialog.NavigationCompleted -= navCompleted;

        dialog.NewWindowRequested += newWindow;
        dialog.NewWindowRequested -= newWindow;

        dialog.WebMessageReceived += webMsg;
        dialog.WebMessageReceived -= webMsg;

        dialog.WebResourceRequested += webRes;
        dialog.WebResourceRequested -= webRes;

        dialog.EnvironmentRequested += envReq;
        dialog.EnvironmentRequested -= envReq;

        dialog.Dispose();
    }

    [Fact]
    public void WebDialog_DownloadRequested_event_subscribe_unsubscribe()
    {
        var host = new MockDialogHost();
        var downloadAdapter = MockWebViewAdapter.CreateWithDownload();
        var dialog = new WebDialog(host, downloadAdapter, _dispatcher);

        bool raised = false;
        EventHandler<DownloadRequestedEventArgs> handler = (_, _) => raised = true;

        dialog.DownloadRequested += handler;
        downloadAdapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
            new Uri("https://example.com/file.zip"), "file.zip", "application/zip", 1024));
        Assert.True(raised);

        raised = false;
        dialog.DownloadRequested -= handler;
        downloadAdapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
            new Uri("https://example.com/file2.zip"), "file2.zip", "application/zip", 2048));
        Assert.False(raised);

        dialog.Dispose();
    }

    [Fact]
    public void WebDialog_PermissionRequested_event_subscribe_unsubscribe()
    {
        var host = new MockDialogHost();
        var permAdapter = MockWebViewAdapter.CreateWithPermission();
        var dialog = new WebDialog(host, permAdapter, _dispatcher);

        bool raised = false;
        EventHandler<PermissionRequestedEventArgs> handler = (_, _) => raised = true;

        dialog.PermissionRequested += handler;
        permAdapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://example.com")));
        Assert.True(raised);

        raised = false;
        dialog.PermissionRequested -= handler;
        permAdapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://example.com")));
        Assert.False(raised);

        dialog.Dispose();
    }

    [Fact]
    public void WebDialog_AdapterDestroyed_event_raised_on_dispose()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        var dialog = new WebDialog(host, adapter, _dispatcher);

        bool raised = false;
        EventHandler handler = (_, _) => raised = true;

        dialog.AdapterDestroyed += handler;
        dialog.Dispose();

        Assert.True(raised);
    }

    [Fact]
    public void WebDialog_AdapterDestroyed_event_unsubscribe()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        var dialog = new WebDialog(host, adapter, _dispatcher);

        bool raised = false;
        EventHandler handler = (_, _) => raised = true;

        dialog.AdapterDestroyed += handler;
        dialog.AdapterDestroyed -= handler;
        dialog.Dispose();

        Assert.False(raised);
    }

    [Fact]
    public void WebDialog_TryGetCommandManager_returns_null_for_basic_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        Assert.Null(dialog.TryGetCommandManager());
    }

    [Fact]
    public void WebDialog_TryGetCommandManager_returns_value_for_command_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithCommands();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        Assert.NotNull(dialog.TryGetCommandManager());
    }

    [Fact]
    public async Task WebDialog_CaptureScreenshotAsync_throws_when_unsupported()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => dialog.CaptureScreenshotAsync());
    }

    [Fact]
    public async Task WebDialog_CaptureScreenshotAsync_with_screenshot_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithScreenshot();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        var data = await dialog.CaptureScreenshotAsync();
        Assert.NotEmpty(data);
    }

    [Fact]
    public async Task WebDialog_PrintToPdfAsync_throws_when_unsupported()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => dialog.PrintToPdfAsync());
    }

    [Fact]
    public async Task WebDialog_PrintToPdfAsync_with_print_adapter()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPrint();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        var data = await dialog.PrintToPdfAsync();
        Assert.NotEmpty(data);
    }

    [Fact]
    public void WebDialog_Rpc_is_null_without_bridge()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        Assert.Null(dialog.Rpc);
    }

    [Fact]
    public void WebDialog_Source_set()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        var dialog = new WebDialog(host, adapter, _dispatcher);

        var uri = new Uri("https://set-source.test");
        dialog.Source = uri;

        Assert.Equal(uri, dialog.Source);

        dialog.Dispose();
    }

    [Fact]
    public async Task FindInPageAsync_throws_when_unsupported()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => core.FindInPageAsync("test"));
    }

    [Fact]
    public async Task FindInPageAsync_throws_on_null_or_empty_text()
    {
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var core = new WebViewCore(adapter, _dispatcher);
        await Assert.ThrowsAsync<ArgumentException>(() => core.FindInPageAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => core.FindInPageAsync(null!));
    }

    [Fact]
    public async Task FindInPageAsync_delegates_to_adapter()
    {
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var core = new WebViewCore(adapter, _dispatcher);
        var opts = new FindInPageOptions { CaseSensitive = true, Forward = false };
        var result = await core.FindInPageAsync("hello", opts);

        Assert.Equal(0, result.ActiveMatchIndex);
        Assert.Equal(3, result.TotalMatches);

        var findAdapter = (MockWebViewAdapterWithFind)adapter;
        Assert.Equal("hello", findAdapter.LastSearchText);
        Assert.True(findAdapter.LastOptions?.CaseSensitive);
        Assert.False(findAdapter.LastOptions?.Forward);
    }

    [Fact]
    public async Task StopFindInPage_throws_when_unsupported()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => core.StopFindInPageAsync());
    }

    [Fact]
    public async Task StopFindInPage_delegates_to_adapter()
    {
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var core = new WebViewCore(adapter, _dispatcher);
        await core.StopFindInPageAsync(false);

        var findAdapter = (MockWebViewAdapterWithFind)adapter;
        Assert.True(findAdapter.StopFindCalled);
        Assert.False(findAdapter.LastClearHighlights);
    }

    [Fact]
    public async Task WebDialog_FindInPageAsync_throws_when_unsupported()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => dialog.FindInPageAsync("test"));
    }

    [Fact]
    public async Task WebDialog_FindInPageAsync_delegates_to_core()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithFind();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        var result = await dialog.FindInPageAsync("hello");
        Assert.Equal(3, result.TotalMatches);
    }

    [Fact]
    public async Task WebDialog_StopFindInPage_throws_when_unsupported()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => dialog.StopFindInPageAsync());
    }

    [Fact]
    public void FindInPageOptions_defaults()
    {
        var opts = new FindInPageOptions();
        Assert.False(opts.CaseSensitive);
        Assert.True(opts.Forward);
    }

    [Fact]
    public void FindInPageEventArgs_defaults()
    {
        var result = new FindInPageEventArgs();
        Assert.Equal(0, result.ActiveMatchIndex);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public void ZoomFactor_default_is_1_without_adapter()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        Assert.Equal(1.0, core.GetZoomFactorAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void ZoomFactor_set_is_noop_without_adapter()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        core.SetZoomFactorAsync(2.0).GetAwaiter().GetResult(); // should not throw
        Assert.Equal(1.0, core.GetZoomFactorAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void ZoomFactor_get_set_with_adapter()
    {
        var adapter = MockWebViewAdapter.CreateWithZoom();
        using var core = new WebViewCore(adapter, _dispatcher);
        Assert.Equal(1.0, core.GetZoomFactorAsync().GetAwaiter().GetResult());

        core.SetZoomFactorAsync(1.5).GetAwaiter().GetResult();
        Assert.Equal(1.5, core.GetZoomFactorAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void ZoomFactor_clamps_to_min_max()
    {
        var adapter = MockWebViewAdapter.CreateWithZoom();
        using var core = new WebViewCore(adapter, _dispatcher);

        core.SetZoomFactorAsync(0.1).GetAwaiter().GetResult(); // below 0.25 min
        Assert.Equal(0.25, core.GetZoomFactorAsync().GetAwaiter().GetResult(), 2);

        core.SetZoomFactorAsync(10.0).GetAwaiter().GetResult(); // above 5.0 max
        Assert.Equal(5.0, core.GetZoomFactorAsync().GetAwaiter().GetResult(), 2);
    }

    [Fact]
    public void ZoomFactorChanged_fires_on_set()
    {
        var adapter = MockWebViewAdapter.CreateWithZoom();
        using var core = new WebViewCore(adapter, _dispatcher);

        double? received = null;
        core.ZoomFactorChanged += (_, z) => received = z;

        core.SetZoomFactorAsync(2.0).GetAwaiter().GetResult();
        Assert.Equal(2.0, received);
    }

    [Fact]
    public void WebDialog_ZoomFactor_delegates_to_core()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithZoom();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        Assert.Equal(1.0, dialog.GetZoomFactorAsync().GetAwaiter().GetResult());
        dialog.SetZoomFactorAsync(1.5).GetAwaiter().GetResult();
        Assert.Equal(1.5, dialog.GetZoomFactorAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public async Task AddPreloadScript_throws_when_unsupported()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => core.AddPreloadScriptAsync("console.log('hi')"));
    }

    [Fact]
    public async Task AddPreloadScript_returns_script_id()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var core = new WebViewCore(adapter, _dispatcher);
        var id = await core.AddPreloadScriptAsync("console.log('hi')");
        Assert.NotNull(id);
        Assert.NotEmpty(id);
    }

    [Fact]
    public async Task AddPreloadScript_prefers_async_adapter_when_available()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var core = new WebViewCore(adapter, _dispatcher);
        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;

        const string script = "console.log('prefer-async')";
        var id = await core.AddPreloadScriptAsync(script);
        await core.RemovePreloadScriptAsync(id);

        Assert.Contains(script, preloadAdapter.AsyncAddedScripts);
        Assert.DoesNotContain(script, preloadAdapter.SyncAddedScripts);
        Assert.Contains(id, preloadAdapter.AsyncRemovedScriptIds);
        Assert.DoesNotContain(id, preloadAdapter.SyncRemovedScriptIds);
    }

    [Fact]
    public async Task RemovePreloadScript_throws_when_unsupported()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => core.RemovePreloadScriptAsync("some-id"));
    }

    [Fact]
    public async Task RemovePreloadScript_removes_script()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var core = new WebViewCore(adapter, _dispatcher);
        var id = await core.AddPreloadScriptAsync("console.log('hi')");
        await core.RemovePreloadScriptAsync(id);

        var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
        Assert.DoesNotContain(id, preloadAdapter.Scripts.Keys);
    }

    [Fact]
    public async Task WebDialog_AddPreloadScript_delegates_to_core()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        var id = await dialog.AddPreloadScriptAsync("console.log('test')");
        Assert.NotNull(id);
    }

    [Fact]
    public async Task WebDialog_AddPreloadScript_throws_when_unsupported()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        using var dialog = new WebDialog(host, adapter, _dispatcher);
        await Assert.ThrowsAsync<NotSupportedException>(() => dialog.AddPreloadScriptAsync("x"));
    }

    [Fact]
    public void ContextMenuRequested_fires_when_adapter_raises()
    {
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var core = new WebViewCore(adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        core.ContextMenuRequested += (_, e) => received = e;

        var args = new ContextMenuRequestedEventArgs
        {
            X = 100, Y = 200,
            LinkUri = new Uri("https://example.com"),
            SelectionText = "hello",
            MediaType = ContextMenuMediaType.None,
            IsEditable = true
        };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.NotNull(received);
        Assert.Equal(100, received!.X);
        Assert.Equal(200, received.Y);
        Assert.Equal("hello", received.SelectionText);
        Assert.True(received.IsEditable);
    }

    [Fact]
    public void ContextMenuRequested_not_fired_without_adapter()
    {
        var adapter = MockWebViewAdapter.Create();
        using var core = new WebViewCore(adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        core.ContextMenuRequested += (_, e) => received = e;

        // No way to trigger it — should stay null
        Assert.Null(received);
    }

    [Fact]
    public void ContextMenuRequested_Handled_flag_propagates()
    {
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var core = new WebViewCore(adapter, _dispatcher);

        core.ContextMenuRequested += (_, e) => e.Handled = true;

        var args = new ContextMenuRequestedEventArgs { X = 10, Y = 20 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.True(args.Handled);
    }

    [Fact]
    public void ContextMenuMediaType_enum_values()
    {
        Assert.Equal(0, (int)ContextMenuMediaType.None);
        Assert.Equal(1, (int)ContextMenuMediaType.Image);
        Assert.Equal(2, (int)ContextMenuMediaType.Video);
        Assert.Equal(3, (int)ContextMenuMediaType.Audio);
    }

    [Fact]
    public void WebDialog_ContextMenuRequested_fires()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithContextMenu();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        ContextMenuRequestedEventArgs? received = null;
        dialog.ContextMenuRequested += (_, e) => received = e;

        var args = new ContextMenuRequestedEventArgs { X = 50, Y = 60 };
        ((MockWebViewAdapterWithContextMenu)adapter).RaiseContextMenu(args);

        Assert.NotNull(received);
        Assert.Equal(50, received!.X);
    }
}
