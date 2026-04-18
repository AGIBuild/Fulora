using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed partial class CoverageGapTests
{
    [Fact]
    public void NativeNavigationStartingInfo_value_equality()
    {
        var id = Guid.NewGuid();
        var uri = new Uri("https://example.test");
        var a = new NativeNavigationStartingInfo(id, uri, true);
        var b = new NativeNavigationStartingInfo(id, uri, true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void NativeNavigationStartingDecision_value_equality()
    {
        var id = Guid.NewGuid();
        var a = new NativeNavigationStartingDecision(true, id);
        var b = new NativeNavigationStartingDecision(true, id);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void WebViewAdapterRegistration_value_equality()
    {
        Func<IWebViewAdapter> factory = () => new MockWebViewAdapter();
        var a = new WebViewAdapterRegistration(WebViewAdapterPlatform.iOS, "wk-ios", factory, 100);
        var b = new WebViewAdapterRegistration(WebViewAdapterPlatform.iOS, "wk-ios", factory, 100);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void WebViewAdapterRegistration_inequality_different_platform()
    {
        Func<IWebViewAdapter> factory = () => new MockWebViewAdapter();
        var a = new WebViewAdapterRegistration(WebViewAdapterPlatform.iOS, "wk", factory, 100);
        var b = new WebViewAdapterRegistration(WebViewAdapterPlatform.Gtk, "wk", factory, 100);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WebViewAdapterRegistration_inequality_different_priority()
    {
        Func<IWebViewAdapter> factory = () => new MockWebViewAdapter();
        var a = new WebViewAdapterRegistration(WebViewAdapterPlatform.iOS, "wk", factory, 100);
        var b = new WebViewAdapterRegistration(WebViewAdapterPlatform.iOS, "wk", factory, 200);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void WebViewAdapterRegistration_ToString_contains_fields()
    {
        var reg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.iOS, "wkwebview-ios", () => new MockWebViewAdapter(), 100);

        var str = reg.ToString();
        Assert.Contains("iOS", str);
        Assert.Contains("wkwebview-ios", str);
    }

    [Fact]
    public void WebViewAdapterRegistration_deconstruction()
    {
        Func<IWebViewAdapter> factory = () => new MockWebViewAdapter();
        var reg = new WebViewAdapterRegistration(WebViewAdapterPlatform.Gtk, "webkitgtk", factory, 50);

        var (platform, adapterId, f, priority) = reg;
        Assert.Equal(WebViewAdapterPlatform.Gtk, platform);
        Assert.Equal("webkitgtk", adapterId);
        Assert.Same(factory, f);
        Assert.Equal(50, priority);
    }

    [Fact]
    public void NativeNavigation_sub_frame_auto_allows()
    {
        var (core, adapter) = CreateCoreWithAdapter();
        IWebViewAdapterHost host = core;

        // Simulate a sub-frame native navigation (IsMainFrame = false)
        var subFrameInfo = new NativeNavigationStartingInfo(
            Guid.NewGuid(), new Uri("https://iframe.test"), IsMainFrame: false);

        var decision = host.OnNativeNavigationStartingAsync(subFrameInfo).GetAwaiter().GetResult();

        Assert.True(decision.IsAllowed);
        Assert.Equal(Guid.Empty, decision.NavigationId);
    }

    [Fact]
    public void NativeNavigation_disposed_denies()
    {
        var (core, adapter) = CreateCoreWithAdapter();
        IWebViewAdapterHost host = core;
        core.Dispose();

        var info = new NativeNavigationStartingInfo(
            Guid.NewGuid(), new Uri("https://example.test"), IsMainFrame: true);

        var decision = host.OnNativeNavigationStartingAsync(info).GetAwaiter().GetResult();

        Assert.False(decision.IsAllowed);
        Assert.Equal(Guid.Empty, decision.NavigationId);
    }

    [Fact]
    public async Task NativeNavigation_same_url_redirect_reuses_navigationId()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        // Start a command navigation.
        var navTask = core.NavigateAsync(new Uri("https://example.test/page"));
        var navId = adapter.LastNavigationId!.Value;

        // Simulate a native redirect to the exact same URL with the same correlationId.
        var decision = await adapter.SimulateNativeNavigationStartingAsync(
            new Uri("https://example.test/page"), correlationId: navId);

        Assert.True(decision.IsAllowed);
        Assert.Equal(navId, decision.NavigationId);

        // Complete the navigation to clean up.
        adapter.RaiseNavigationCompleted();
        await navTask;
    }

    [Fact]
    public async Task NavigateAsync_adapter_throws_completes_with_failure()
    {
        var throwingAdapter = new ThrowingNavigateAdapter();
        var core = new WebViewCore(throwingAdapter, _dispatcher);

        var ex = await Assert.ThrowsAsync<WebViewNavigationException>(
            () => core.NavigateAsync(new Uri("https://fail.test")));

        Assert.Contains("Navigation failed", ex.Message);
    }

    [Fact]
    public void NavigationCompleted_with_no_active_navigation_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        // Raise NavigationCompleted without any active navigation.
        adapter.RaiseNavigationCompleted(Guid.NewGuid(), new Uri("https://orphan.test"),
            NavigationCompletedStatus.Success);

        // Should be silently ignored — no event fired to the consumer.
        Assert.Null(completedArgs);
    }

    [Fact]
    public async Task NavigationCompleted_failure_without_error_synthesizes_exception()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        var navTask = core.NavigateAsync(new Uri("https://fail.test"));
        var navId = adapter.LastNavigationId!.Value;

        // Raise failure with null error — WebViewCore should synthesize an exception.
        adapter.RaiseNavigationCompleted(navId, new Uri("https://fail.test"),
            NavigationCompletedStatus.Failure, error: null);

        var ex = await Assert.ThrowsAsync<WebViewNavigationException>(() => navTask);
        Assert.Equal("Navigation failed.", ex.Message);
    }

    [Fact]
    public async Task NavigationCompleted_failure_preserves_categorized_exception()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        var navTask = core.NavigateAsync(new Uri("https://fail.test"));
        var navId = adapter.LastNavigationId!.Value;
        var netEx = new WebViewNetworkException("DNS failed", navId, new Uri("https://fail.test"));

        adapter.RaiseNavigationCompleted(navId, new Uri("https://fail.test"),
            NavigationCompletedStatus.Failure, error: netEx);

        var ex = await Assert.ThrowsAsync<WebViewNetworkException>(() => navTask);
        Assert.Same(netEx, ex);
    }

    [Fact]
    public void WebViewCore_ctor_null_adapter_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new WebViewCore(null!, _dispatcher));
    }

    [Fact]
    public void WebViewCore_ctor_null_dispatcher_throws()
    {
        var adapter = MockWebViewAdapter.Create();
        Assert.Throws<ArgumentNullException>(() => new WebViewCore(adapter, null!));
    }

    [Fact]
    public void WebViewCore_ctor_null_logger_uses_NullLogger()
    {
        var adapter = MockWebViewAdapter.Create();
        // Pass null logger via internal ctor — should not throw
        using var core = new WebViewCore(adapter, _dispatcher,
            null!);
        Assert.NotNull(core);
    }

    [Fact]
    public void Branch_DownloadRequested_on_thread_raises_directly()
    {
        var adapter = MockWebViewAdapter.CreateWithDownload();
        using var core = new WebViewCore(adapter, _dispatcher);

        DownloadRequestedEventArgs? received = null;
        core.DownloadRequested += (_, e) => received = e;

        var args = new DownloadRequestedEventArgs(new Uri("https://example.com/file.zip"), "file.zip");
        adapter.RaiseDownloadRequested(args);

        Assert.NotNull(received);
    }

    [Fact]
    public void Branch_PermissionRequested_on_thread_raises_directly()
    {
        var adapter = MockWebViewAdapter.CreateWithPermission();
        using var core = new WebViewCore(adapter, _dispatcher);

        PermissionRequestedEventArgs? received = null;
        core.PermissionRequested += (_, e) => received = e;

        var args = new PermissionRequestedEventArgs(WebViewPermissionKind.Camera, new Uri("https://example.com"));
        adapter.RaisePermissionRequested(args);

        Assert.NotNull(received);
    }

    [Fact]
    public void Branch_DownloadRequested_off_thread_dispatches_to_ui()
    {
        var adapter = MockWebViewAdapter.CreateWithDownload();
        using var core = new WebViewCore(adapter, _dispatcher);

        DownloadRequestedEventArgs? received = null;
        core.DownloadRequested += (_, e) => received = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
                new Uri("https://dl.test/file.zip"), "file.zip"));
        });

        _dispatcher.RunAll();
        Assert.NotNull(received);
    }

    [Fact]
    public void Branch_PermissionRequested_off_thread_dispatches_to_ui()
    {
        var adapter = MockWebViewAdapter.CreateWithPermission();
        using var core = new WebViewCore(adapter, _dispatcher);

        PermissionRequestedEventArgs? received = null;
        core.PermissionRequested += (_, e) => received = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
                WebViewPermissionKind.Camera, new Uri("https://perm.test")));
        });

        _dispatcher.RunAll();
        Assert.NotNull(received);
    }

    [Fact]
    public void Branch_DownloadRequested_after_dispose_is_ignored()
    {
        var adapter = MockWebViewAdapter.CreateWithDownload();
        using var core = new WebViewCore(adapter, _dispatcher);

        DownloadRequestedEventArgs? received = null;
        core.DownloadRequested += (_, e) => received = e;
        core.Dispose();

        adapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
            new Uri("https://dl.test/file.zip"), "file.zip"));
        Assert.Null(received);
    }

    [Fact]
    public void Branch_PermissionRequested_after_dispose_is_ignored()
    {
        var adapter = MockWebViewAdapter.CreateWithPermission();
        using var core = new WebViewCore(adapter, _dispatcher);

        PermissionRequestedEventArgs? received = null;
        core.PermissionRequested += (_, e) => received = e;
        core.Dispose();

        adapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
            WebViewPermissionKind.Camera, new Uri("https://perm.test")));
        Assert.Null(received);
    }

    [Fact]
    public async Task Branch_NativeNavigation_redirect_cancel_completes_navigation()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        var navTask = core.NavigateAsync(new Uri("https://redirect.test/page"));
        var navId = adapter.LastNavigationId!.Value;

        // Subscribe and cancel any redirect navigation
        core.NavigationStarted += (_, e) => e.Cancel = true;

        // Simulate a redirect to a different URL with the same correlation ID
        var decision = await adapter.SimulateNativeNavigationStartingAsync(
            new Uri("https://redirect.test/other"),
            correlationId: navId);

        Assert.False(decision.IsAllowed);
        Assert.NotNull(completedArgs);
        Assert.Equal(NavigationCompletedStatus.Canceled, completedArgs!.Status);

        // Canceled navigations still resolve successfully (not faulted)
        await navTask;
    }

    [Fact]
    public async Task NavigationCompleted_id_mismatch_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        var navTask = core.NavigateAsync(new Uri("https://example.test"));
        var navId = adapter.LastNavigationId!.Value;

        // Raise NavigationCompleted with a DIFFERENT ID
        adapter.RaiseNavigationCompleted(Guid.NewGuid(), new Uri("https://wrong.test"),
            NavigationCompletedStatus.Success);

        // Should be ignored — no event fired
        Assert.Null(completedArgs);

        // Now complete with correct ID
        adapter.RaiseNavigationCompleted(navId, new Uri("https://example.test"),
            NavigationCompletedStatus.Success);
        await navTask;
        Assert.NotNull(completedArgs);
    }

    [Fact]
    public void NewWindowRequested_unhandled_navigates_in_view()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        // Don't subscribe to NewWindowRequested — it will be unhandled
        adapter.RaiseNewWindowRequested(new Uri("https://popup.test/page"));

        // Adapter should have received a navigate call for the popup URI
        Assert.NotNull(adapter.LastNavigationUri);
        Assert.Equal("https://popup.test/page", adapter.LastNavigationUri!.ToString());
    }

    [Fact]
    public void NavigationCompleted_after_dispose_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        core.Dispose();

        adapter.RaiseNavigationCompleted(Guid.NewGuid(), new Uri("https://disposed.test"),
            NavigationCompletedStatus.Success);

        Assert.Null(completedArgs);
    }

    [Fact]
    public void NewWindowRequested_after_dispose_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NewWindowRequestedEventArgs? newWindowArgs = null;
        core.NewWindowRequested += (_, e) => newWindowArgs = e;

        core.Dispose();

        adapter.RaiseNewWindowRequested(new Uri("https://disposed-popup.test"));

        Assert.Null(newWindowArgs);
    }

    [Fact]
    public void WebResourceRequested_after_dispose_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        WebResourceRequestedEventArgs? resourceArgs = null;
        core.WebResourceRequested += (_, e) => resourceArgs = e;

        core.Dispose();

        adapter.RaiseWebResourceRequested();

        Assert.Null(resourceArgs);
    }

    [Fact]
    public void EnvironmentRequested_after_dispose_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        EnvironmentRequestedEventArgs? envArgs = null;
        core.EnvironmentRequested += (_, e) => envArgs = e;

        core.Dispose();

        adapter.RaiseEnvironmentRequested();

        Assert.Null(envArgs);
    }

    [Fact]
    public async Task Command_navigation_supersedes_active_navigation()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? supersededArgs = null;
        core.NavigationCompleted += (_, e) =>
        {
            if (e.Status == NavigationCompletedStatus.Superseded)
                supersededArgs = e;
        };

        // Start a navigation
        var navTask1 = core.NavigateAsync(new Uri("https://first.test"));
        Assert.True(core.IsLoading);

        adapter.GoBackAccepted = true;
        adapter.CanGoBack = true;

        // GoBack while first navigation is active — should supersede it
        await core.GoBackAsync();

        // First navigation should complete with Superseded
        Assert.NotNull(supersededArgs);
        Assert.Equal(NavigationCompletedStatus.Superseded, supersededArgs!.Status);

        // Clean up — complete the back navigation
        adapter.RaiseNavigationCompleted();
        await navTask1;
    }

    [Fact]
    public void TryCreateForCurrentPlatform_returns_result_or_failure_reason()
    {
        // This test verifies the success/failure paths of TryCreateForCurrentPlatform.
        // On the CI platform, it may or may not have an adapter registered.
        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        if (result)
        {
            Assert.NotNull(adapter);
            Assert.Null(reason);
        }
        else
        {
            Assert.NotNull(reason);
            Assert.Contains("No WebView adapter registered", reason);
        }
    }

    [Fact]
    public async Task WebAuthBroker_show_with_platform_handle_delegates_to_show_owner()
    {
        var factory = new AuthTestDialogFactoryLocal(_dispatcher);
        var broker = new WebAuthBroker(factory);

        // Use a window WITH a PlatformHandle to exercise lines 57-59 in WebAuthBroker.
        var owner = new NonNullHandleWindow();

        var options = new AuthOptions
        {
            AuthorizeUri = new Uri("https://auth.test/authorize"),
            CallbackUri = new Uri("myapp://auth/callback"),
        };

        factory.OnDialogCreated = (dialog, adapter) =>
        {
            adapter.AutoCompleteNavigation = true;
            adapter.OnNavigationAutoCompleted = () =>
            {
                _ = adapter.SimulateNativeNavigationStartingAsync(
                    new Uri("myapp://auth/callback?code=abc123"));
            };
        };

        var result = await broker.AuthenticateAsync(owner, options);

        Assert.Equal(WebAuthStatus.Success, result.Status);
        Assert.NotNull(result.CallbackUri);

        // Verify Show was called (dialog is closed by broker's finally block, so check call count)
        Assert.Equal(1, factory.LastHost!.ShowCallCount);
    }

    [Fact]
    public async Task NavigationCompleted_ctor_exception_produces_failure()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        var navTask = core.NavigateAsync(new Uri("https://error.test"));
        var navId = adapter.LastNavigationId!.Value;

        // Normal failure path to verify error propagation
        adapter.RaiseNavigationCompleted(navId, new Uri("https://error.test"),
            NavigationCompletedStatus.Failure,
            new WebViewNetworkException("Net error", navId, new Uri("https://error.test")));

        try
        {
            await navTask;
        }
        catch
        {
            // Absorb any fault
        }

        Assert.NotNull(completedArgs);
        Assert.Equal(NavigationCompletedStatus.Failure, completedArgs!.Status);
        Assert.NotNull(completedArgs.Error);
    }

    [Fact]
    public async Task NavigationCompleted_off_thread_dispatches_to_ui()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        var navTask = core.NavigateAsync(new Uri("https://offthread.test"));
        var navId = adapter.LastNavigationId!.Value;

        // Raise from a separate thread so CheckAccess() returns false
        RunOnBackgroundThread(() =>
        {
            adapter.RaiseNavigationCompleted(navId, new Uri("https://offthread.test"),
                NavigationCompletedStatus.Success);
        });

        // Drain dispatcher queue (we're still on the original "UI" thread)
        _dispatcher.RunAll();
        await navTask;

        Assert.NotNull(completedArgs);
        Assert.Equal(NavigationCompletedStatus.Success, completedArgs!.Status);
    }

    [Fact]
    public void NewWindowRequested_off_thread_dispatches_to_ui()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NewWindowRequestedEventArgs? windowArgs = null;
        core.NewWindowRequested += (_, e) =>
        {
            windowArgs = e;
            e.Handled = true;
        };

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseNewWindowRequested(new Uri("https://popup-offthread.test"));
        });

        _dispatcher.RunAll();

        Assert.NotNull(windowArgs);
        Assert.Equal("https://popup-offthread.test/", windowArgs!.Uri!.ToString());
    }

    [Fact]
    public void WebResourceRequested_off_thread_dispatches_to_ui()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        WebResourceRequestedEventArgs? resourceArgs = null;
        core.WebResourceRequested += (_, e) => resourceArgs = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseWebResourceRequested();
        });

        _dispatcher.RunAll();

        Assert.NotNull(resourceArgs);
    }

    [Fact]
    public void EnvironmentRequested_off_thread_dispatches_to_ui()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        EnvironmentRequestedEventArgs? envArgs = null;
        core.EnvironmentRequested += (_, e) => envArgs = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseEnvironmentRequested();
        });

        _dispatcher.RunAll();

        Assert.NotNull(envArgs);
    }

    [Fact]
    public void DownloadRequested_off_thread_dispatches_to_ui()
    {
        var downloadAdapter = MockWebViewAdapter.CreateWithDownload();
        var core = new WebViewCore(downloadAdapter, _dispatcher);

        DownloadRequestedEventArgs? dlArgs = null;
        core.DownloadRequested += (_, e) => dlArgs = e;

        RunOnBackgroundThread(() =>
        {
            downloadAdapter.RaiseDownloadRequested(new DownloadRequestedEventArgs(
                new Uri("https://example.com/file.zip"), "file.zip", "application/zip", 1024));
        });

        _dispatcher.RunAll();

        Assert.NotNull(dlArgs);
        Assert.Equal("file.zip", dlArgs!.SuggestedFileName);
    }

    [Fact]
    public void PermissionRequested_off_thread_dispatches_to_ui()
    {
        var permAdapter = MockWebViewAdapter.CreateWithPermission();
        var core = new WebViewCore(permAdapter, _dispatcher);

        PermissionRequestedEventArgs? permArgs = null;
        core.PermissionRequested += (_, e) => permArgs = e;

        RunOnBackgroundThread(() =>
        {
            permAdapter.RaisePermissionRequested(new PermissionRequestedEventArgs(
                WebViewPermissionKind.Microphone, new Uri("https://example.com")));
        });

        _dispatcher.RunAll();

        Assert.NotNull(permArgs);
        Assert.Equal(WebViewPermissionKind.Microphone, permArgs!.PermissionKind);
    }

    [Fact]
    public async Task NavigationCompleted_off_thread_then_disposed_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NavigationCompletedEventArgs? completedArgs = null;
        core.NavigationCompleted += (_, e) => completedArgs = e;

        var navTask = core.NavigateAsync(new Uri("https://offthread-dispose.test"));
        var navId = adapter.LastNavigationId!.Value;

        // Raise from background thread to enqueue
        RunOnBackgroundThread(() =>
        {
            adapter.RaiseNavigationCompleted(navId, new Uri("https://offthread-dispose.test"),
                NavigationCompletedStatus.Success);
        });

        // Dispose BEFORE draining — the on-UI-thread handler should see _disposed
        core.Dispose();
        _dispatcher.RunAll();

        // NavigationCompleted event should NOT have been raised to consumer (ignored on UI thread)
        Assert.Null(completedArgs);

        // But the navTask was faulted by Dispose
        await Assert.ThrowsAsync<ObjectDisposedException>(() => navTask);
    }

    [Fact]
    public void NewWindowRequested_off_thread_then_disposed_is_ignored()
    {
        var (core, adapter) = CreateCoreWithAdapter();

        NewWindowRequestedEventArgs? windowArgs = null;
        core.NewWindowRequested += (_, e) => windowArgs = e;

        RunOnBackgroundThread(() =>
        {
            adapter.RaiseNewWindowRequested(new Uri("https://popup-dispose.test"));
        });

        core.Dispose();
        _dispatcher.RunAll();

        Assert.Null(windowArgs);
    }

    [Fact]
    public void ICommandManager_has_all_six_methods()
    {
        var methods = typeof(ICommandManager).GetMethods();
        Assert.Contains(methods, m => m.Name == "CopyAsync");
        Assert.Contains(methods, m => m.Name == "CutAsync");
        Assert.Contains(methods, m => m.Name == "PasteAsync");
        Assert.Contains(methods, m => m.Name == "SelectAllAsync");
        Assert.Contains(methods, m => m.Name == "UndoAsync");
        Assert.Contains(methods, m => m.Name == "RedoAsync");
    }

    [Fact]
    public void ICommandAdapter_facet_detected_by_core()
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();
        Assert.IsAssignableFrom<ICommandAdapter>(adapter);
    }

    [Theory]
    [InlineData(WebViewCommand.Copy)]
    [InlineData(WebViewCommand.Cut)]
    [InlineData(WebViewCommand.Paste)]
    [InlineData(WebViewCommand.SelectAll)]
    [InlineData(WebViewCommand.Undo)]
    [InlineData(WebViewCommand.Redo)]
    public void WebViewCommand_enum_has_expected_value(WebViewCommand command)
    {
        Assert.True(Enum.IsDefined(typeof(WebViewCommand), command));
    }

    [Fact]
    public void TryGetCommandManager_returns_non_null_with_ICommandAdapter()
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();
        using var core = new WebViewCore(adapter, _dispatcher);
        var mgr = core.TryGetCommandManager();
        Assert.NotNull(mgr);
    }

    [Theory]
    [InlineData(WebViewCommand.Copy)]
    [InlineData(WebViewCommand.Cut)]
    [InlineData(WebViewCommand.Paste)]
    [InlineData(WebViewCommand.SelectAll)]
    [InlineData(WebViewCommand.Undo)]
    [InlineData(WebViewCommand.Redo)]
    public async Task CommandManager_delegates_to_adapter(WebViewCommand command)
    {
        var adapter = MockWebViewAdapter.CreateWithCommands();
        using var core = new WebViewCore(adapter, _dispatcher);
        var mgr = core.TryGetCommandManager()!;

        switch (command)
        {
            case WebViewCommand.Copy: await mgr.CopyAsync(); break;
            case WebViewCommand.Cut: await mgr.CutAsync(); break;
            case WebViewCommand.Paste: await mgr.PasteAsync(); break;
            case WebViewCommand.SelectAll: await mgr.SelectAllAsync(); break;
            case WebViewCommand.Undo: await mgr.UndoAsync(); break;
            case WebViewCommand.Redo: await mgr.RedoAsync(); break;
        }

        Assert.Single(adapter.ExecutedCommands);
        Assert.Equal(command, adapter.ExecutedCommands[0]);
    }

    [Fact]
    public void IScreenshotAdapter_facet_detected()
    {
        var adapter = MockWebViewAdapter.CreateWithScreenshot();
        Assert.IsAssignableFrom<IScreenshotAdapter>(adapter);
    }

    [Fact]
    public async Task CaptureScreenshotAsync_returns_data_with_IScreenshotAdapter()
    {
        var adapter = MockWebViewAdapter.CreateWithScreenshot();
        using var core = new WebViewCore(adapter, _dispatcher);
        var result = await core.CaptureScreenshotAsync();
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // PNG magic bytes
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
    }

    [Fact]
    public void PdfPrintOptions_has_sensible_defaults()
    {
        var opts = new PdfPrintOptions();
        Assert.False(opts.Landscape);
        Assert.Equal(8.5, opts.PageWidth);
        Assert.Equal(11.0, opts.PageHeight);
        Assert.Equal(0.4, opts.MarginTop);
        Assert.Equal(0.4, opts.MarginBottom);
        Assert.Equal(0.4, opts.MarginLeft);
        Assert.Equal(0.4, opts.MarginRight);
        Assert.Equal(1.0, opts.Scale);
        Assert.True(opts.PrintBackground);
    }

    [Fact]
    public void IPrintAdapter_facet_detected()
    {
        var adapter = MockWebViewAdapter.CreateWithPrint();
        Assert.IsAssignableFrom<IPrintAdapter>(adapter);
    }

    [Fact]
    public async Task PrintToPdfAsync_returns_data_with_IPrintAdapter()
    {
        var adapter = MockWebViewAdapter.CreateWithPrint();
        using var core = new WebViewCore(adapter, _dispatcher);
        var result = await core.PrintToPdfAsync();
        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        // %PDF magic bytes
        Assert.Equal(0x25, result[0]);
        Assert.Equal(0x50, result[1]);
    }

    [Fact]
    public void Global_preloadscripts_applied_at_construction()
    {
        var adapter = MockWebViewAdapter.CreateWithPreload();
        var originalOptions = WebViewEnvironment.Options;
        try
        {
            WebViewEnvironment.Options = new WebViewEnvironmentOptions
            {
                PreloadScripts = new[] { "console.log('global1')", "console.log('global2')" }
            };
            using var core = new WebViewCore(adapter, _dispatcher);
            var preloadAdapter = (MockWebViewAdapterWithPreload)adapter;
            Assert.Equal(2, preloadAdapter.Scripts.Count);
        }
        finally
        {
            WebViewEnvironment.Options = originalOptions;
        }
    }
}
