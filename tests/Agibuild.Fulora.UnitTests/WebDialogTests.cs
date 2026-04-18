using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Contract tests for WebDialog — verifies IWebDialog lifecycle, window management,
/// and IWebView delegation through the MockDialogHost.
/// </summary>
public sealed class WebDialogTests
{
    private readonly TestDispatcher _dispatcher = new();

    private (WebDialog Dialog, MockDialogHost Host, MockWebViewAdapter Adapter) CreateDialog()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.Create();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        return (dialog, host, adapter);
    }

    [Fact]
    public void Title_get_set_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.Title = "Test Title";
        Assert.Equal("Test Title", dialog.Title);
        Assert.Equal("Test Title", host.Title);
    }

    [Fact]
    public void CanUserResize_get_set_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.CanUserResize = true;
        Assert.True(dialog.CanUserResize);
        Assert.True(host.CanUserResize);
    }

    [Fact]
    public void Show_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.Show();
        Assert.True(host.IsShown);
        Assert.Equal(1, host.ShowCallCount);
    }

    [Fact]
    public void Close_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.Show();
        dialog.Close();
        Assert.True(host.IsClosed);
        Assert.Equal(1, host.CloseCallCount);
    }

    [Fact]
    public void Resize_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        var result = dialog.Resize(800, 600);
        Assert.True(result);
        Assert.Equal(1, host.ResizeCallCount);
        Assert.Equal((800, 600), host.LastResize);
    }

    [Fact]
    public void Move_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        var result = dialog.Move(100, 200);
        Assert.True(result);
        Assert.Equal(1, host.MoveCallCount);
        Assert.Equal((100, 200), host.LastMove);
    }

    [Fact]
    public void Closing_event_raised_when_host_closes()
    {
        var (dialog, host, _) = CreateDialog();
        bool closingRaised = false;
        dialog.Closing += (_, _) => closingRaised = true;

        host.SimulateUserClose();
        Assert.True(closingRaised);
    }

    [Fact]
    public void Dispose_closes_host_and_core()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.Show();
        dialog.Dispose();

        Assert.True(host.IsClosed);
    }

    [Fact]
    public void Dispose_does_not_raise_Closing_event()
    {
        var (dialog, _, _) = CreateDialog();
        bool closingRaised = false;
        dialog.Closing += (_, _) => closingRaised = true;

        // Dispose unsubscribes from HostClosing before calling Close.
        dialog.Dispose();

        // The Closing event should NOT fire during programmatic dispose.
        // (Implementation detail: Dispose unsubscribes then closes.)
        Assert.False(closingRaised);
    }

    [Fact]
    public void ChannelId_is_non_empty()
    {
        var (dialog, _, _) = CreateDialog();
        Assert.NotEqual(Guid.Empty, dialog.ChannelId);
    }

    [Fact]
    public void NavigateAsync_delegates_to_core_and_adapter()
    {
        var (dialog, _, adapter) = CreateDialog();
        var uri = new Uri("https://example.com");

        // Start navigation (it won't complete without raising NavigationCompleted).
        var navTask = Task.Run(() => dialog.NavigateAsync(uri), TestContext.Current.CancellationToken);
        DispatcherTestPump.WaitUntil(_dispatcher, () => adapter.LastNavigationId.HasValue);

        // Adapter should have received the navigation request.
        Assert.NotNull(adapter.LastNavigationId);
        Assert.Equal(uri, adapter.LastNavigationUri);

        // Complete the navigation so the task resolves.
        adapter.RaiseNavigationCompleted();
        DispatcherTestPump.WaitUntil(_dispatcher, () => navTask.IsCompleted);
        navTask.GetAwaiter().GetResult();
    }

    [Fact]
    public void InvokeScriptAsync_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.ScriptResult = "42";

        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.InvokeScriptAsync("return 42"));
        Assert.Equal("42", result);
    }

    [Fact]
    public async Task TryGetWebViewHandleAsync_delegates_to_core()
    {
        var (dialog, _, _) = CreateDialog();

        var handle = await dialog.TryGetWebViewHandleAsync();

        Assert.Null(handle);
    }

    [Fact]
    public void Source_get_set_delegates_to_core()
    {
        var (dialog, _, _) = CreateDialog();

        // Default source
        Assert.Equal(new Uri("about:blank"), dialog.Source);
    }

    [Fact]
    public void Show_with_owner_delegates_to_host()
    {
        var (dialog, host, _) = CreateDialog();

        var result = dialog.Show(new TestPlatformHandle());
        Assert.True(result);
        Assert.True(host.IsShown);
        Assert.Equal(1, host.ShowCallCount);
    }

    [Fact]
    public void GoBack_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.CanGoBack = true;
        adapter.GoBackAccepted = true;

        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.GoBackAsync());
        Assert.True(result);
        Assert.Equal(1, adapter.GoBackCallCount);
    }

    [Fact]
    public void GoForward_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.CanGoForward = true;
        adapter.GoForwardAccepted = true;

        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.GoForwardAsync());
        Assert.True(result);
        Assert.Equal(1, adapter.GoForwardCallCount);
    }

    [Fact]
    public void Refresh_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.RefreshAccepted = true;

        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.RefreshAsync());
        Assert.True(result);
        Assert.Equal(1, adapter.RefreshCallCount);
    }

    [Fact]
    public void Stop_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.StopAccepted = true;
        adapter.AutoCompleteNavigation = false;

        // Need an active navigation for Stop to work
        var navTask = Task.Run(() => dialog.NavigateAsync(new Uri("https://example.com")), TestContext.Current.CancellationToken);
        DispatcherTestPump.WaitUntil(_dispatcher, () => adapter.LastNavigationId.HasValue);
        var result = DispatcherTestPump.Run(_dispatcher, () => dialog.StopAsync());
        Assert.True(result);
        Assert.Equal(1, adapter.StopCallCount);
        adapter.RaiseNavigationCompleted(NavigationCompletedStatus.Canceled);
        dialog.Dispose();
    }

    [Fact]
    public void CanGoBack_CanGoForward_IsLoading_delegate_to_core()
    {
        var (dialog, _, _) = CreateDialog();

        // Defaults
        Assert.False(dialog.CanGoBack);
        Assert.False(dialog.CanGoForward);
        Assert.False(dialog.IsLoading);
    }

    [Fact]
    public void NavigateToStringAsync_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.AutoCompleteNavigation = true;

        DispatcherTestPump.Run(_dispatcher, () => dialog.NavigateToStringAsync("<h1>Hello</h1>"));
        Assert.NotNull(adapter.LastNavigationId);
    }

    [Fact]
    public void NavigateToStringAsync_with_baseUrl_delegates_to_core()
    {
        var (dialog, _, adapter) = CreateDialog();
        adapter.AutoCompleteNavigation = true;
        var baseUrl = new Uri("https://base.test/");

        DispatcherTestPump.Run(_dispatcher, () => dialog.NavigateToStringAsync("<h1>Hello</h1>", baseUrl));
        Assert.Equal(baseUrl, adapter.LastBaseUrl);
    }

    [Fact]
    public async Task RemovePreloadScriptAsync_delegates_to_core()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPreload();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var id = await dialog.AddPreloadScriptAsync("console.log('remove-me')");
        await dialog.RemovePreloadScriptAsync(id);

        var preloadAdapter = Assert.IsType<MockWebViewAdapterWithPreload>(adapter);
        Assert.DoesNotContain(id, preloadAdapter.Scripts.Keys);
    }

    [Fact]
    public void NavigationStarted_event_delegation()
    {
        var (dialog, _, adapter) = CreateDialog();
        var raised = false;
        dialog.NavigationStarted += (_, _) => raised = true;

        adapter.AutoCompleteNavigation = true;
        DispatcherTestPump.Run(_dispatcher, () => dialog.NavigateAsync(new Uri("https://example.com")));
        Assert.True(raised);
    }

    [Fact]
    public void NavigationCompleted_event_delegation()
    {
        var (dialog, _, adapter) = CreateDialog();
        var raised = false;
        dialog.NavigationCompleted += (_, _) => raised = true;

        adapter.AutoCompleteNavigation = true;
        DispatcherTestPump.Run(_dispatcher, () => dialog.NavigateAsync(new Uri("https://example.com")));
        Assert.True(raised);
    }

    [Fact]
    public void NewWindowRequested_event_delegation()
    {
        var (dialog, _, adapter) = CreateDialog();
        var raised = false;
        dialog.NewWindowRequested += (_, _) => raised = true;

        adapter.RaiseNewWindowRequested(new Uri("https://popup.test"));
        Assert.True(raised);
    }

    [Fact]
    public void WebResourceRequested_event_delegation()
    {
        var (dialog, _, adapter) = CreateDialog();
        var raised = false;
        dialog.WebResourceRequested += (_, _) => raised = true;

        adapter.RaiseWebResourceRequested();
        Assert.True(raised);
    }

    [Fact]
    public void EnvironmentRequested_event_delegation()
    {
        var (dialog, _, adapter) = CreateDialog();
        var raised = false;
        dialog.EnvironmentRequested += (_, _) => raised = true;

        adapter.RaiseEnvironmentRequested();
        Assert.True(raised);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var (dialog, host, _) = CreateDialog();

        dialog.Dispose();
        dialog.Dispose(); // second dispose should be no-op

        Assert.Equal(1, host.CloseCallCount);
    }

    [Fact]
    public void MockWebDialogFactory_creates_trackable_dialogs()
    {
        var factory = new MockWebDialogFactory(_dispatcher);

        var dialog1 = factory.Create();
        var dialog2 = factory.Create();

        Assert.Equal(2, factory.CreatedDialogs.Count);
        Assert.NotSame(dialog1, dialog2);
    }

    private sealed class TestPlatformHandle : INativeHandle
    {
        public nint Handle => nint.Zero;
        public string HandleDescriptor => "Test";
    }
}
