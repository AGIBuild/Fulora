using System;
using System.Reflection;
using Agibuild.Fulora.Integration.Tests.ViewModels;
using Agibuild.Fulora.Integration.Tests.Views;
using Agibuild.Fulora.Testing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

public sealed class WebViewHostClosingLifecycleIntegrationTests
{
    [AvaloniaFact]
    public void ShouldDetachForHostWindowClosing_honors_close_intent()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var userCloseReason = unchecked((WindowCloseReason)(-1));

            Assert.True(WebView.ShouldDetachForHostWindowClosing(
                isProgrammatic: false,
                closeReason: userCloseReason));
            Assert.True(WebView.ShouldDetachForHostWindowClosing(
                isProgrammatic: true,
                closeReason: userCloseReason));
            Assert.True(WebView.ShouldDetachForHostWindowClosing(
                isProgrammatic: false,
                closeReason: WindowCloseReason.ApplicationShutdown));
            Assert.True(WebView.ShouldDetachForHostWindowClosing(
                isProgrammatic: false,
                closeReason: WindowCloseReason.OSShutdown));
        });
    }

    [AvaloniaFact]
    public void HandleHostWindowClosing_detaches_once_and_marks_core_not_attached()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var dispatcher = new TestDispatcher();
            var adapter = MockWebViewAdapter.Create();
            var core = new WebViewCore(adapter, dispatcher);
            core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

            var control = new WebView();
            control.TestOnlyAttachCore(core);
            control.TestOnlySetCoreAttached(true);

            var detached = control.HandleHostWindowClosing(
                isProgrammatic: false,
                closeReason: unchecked((WindowCloseReason)(-1)));
            Assert.True(detached);
            Assert.Equal(1, adapter.DetachCallCount);
            Assert.False(GetCoreAttached(control));

            var second = control.HandleHostWindowClosing(
                isProgrammatic: false,
                closeReason: unchecked((WindowCloseReason)(-1)));
            Assert.False(second);
            Assert.Equal(1, adapter.DetachCallCount);

            core.Dispose();
        });
    }

    [AvaloniaFact]
    public void HandleHostWindowClosing_stops_forwarding_core_events_after_early_detach()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var dispatcher = new TestDispatcher();
            var adapter = MockWebViewAdapter.Create();
            var core = new WebViewCore(adapter, dispatcher);
            core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

            var control = new WebView();
            var navigationStartedCalls = 0;
            control.NavigationStarted += (_, _) => navigationStartedCalls++;
            control.TestOnlyAttachCore(core);
            control.TestOnlySetCoreAttached(true);
            control.TestOnlySubscribeCoreEvents();

            ((IWebViewCoreNavigationHost)core).RaiseNavigationStarting(
                new NavigationStartingEventArgs(Guid.NewGuid(), new Uri("https://before-close.test")));
            Assert.Equal(1, navigationStartedCalls);

            var detached = control.HandleHostWindowClosing(
                isProgrammatic: false,
                closeReason: unchecked((WindowCloseReason)(-1)));

            Assert.True(detached);
            Assert.Equal(1, adapter.DetachCallCount);
            Assert.False(GetCoreAttached(control));

            ((IWebViewCoreNavigationHost)core).RaiseNavigationStarting(
                new NavigationStartingEventArgs(Guid.NewGuid(), new Uri("https://after-close.test")));
            Assert.Equal(1, navigationStartedCalls);

            core.Dispose();
        });
    }

    [AvaloniaFact]
    public void HandleHostWindowClosing_does_not_forward_adapter_destroyed_during_early_detach()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var dispatcher = new TestDispatcher();
            var adapter = MockWebViewAdapter.Create();
            var core = new WebViewCore(adapter, dispatcher);
            core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

            var control = new WebView();
            var adapterDestroyedCalls = 0;
            control.AdapterDestroyed += (_, _) => adapterDestroyedCalls++;
            control.TestOnlyAttachCore(core);
            control.TestOnlySetCoreAttached(true);
            control.TestOnlySubscribeCoreEvents();

            var detached = control.HandleHostWindowClosing(
                isProgrammatic: false,
                closeReason: unchecked((WindowCloseReason)(-1)));

            Assert.True(detached);
            Assert.Equal(1, adapter.DetachCallCount);
            Assert.False(GetCoreAttached(control));
            Assert.Equal(0, adapterDestroyedCalls);

            core.Dispose();
        });
    }

    [AvaloniaFact]
    public void WebView2SmokeView_detaches_adapter_on_host_window_closing()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var vm = new WebView2SmokeViewModel(_ => { });
            var adapter = MockWebViewAdapter.Create();
            adapter.Initialize(vm);
            adapter.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));
            SetPrivateField(vm, "_adapter", adapter);

            var view = new WebView2SmokeView { DataContext = vm };
            var window = new Window
            {
                Width = 640,
                Height = 480,
                Content = view
            };

            window.Show();
            window.Close();

            Assert.Equal(1, adapter.DetachCallCount);
        });
    }

    [AvaloniaFact]
    public void MainView_switching_from_smoke_to_feature_detaches_smoke_adapter()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        AvaloniaUiThreadRunner.Run(() =>
        {
            var vm = new MainViewModel();
            var smokeVm = vm.WebView2Smoke;

            var adapter = MockWebViewAdapter.Create();
            adapter.Initialize(smokeVm);
            adapter.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));
            SetPrivateField(smokeVm, "_adapter", adapter);

            var view = new MainView
            {
                DataContext = vm
            };

            vm.SelectedTabIndex = 2;
            vm.SelectedTabIndex = 3;

            Assert.Equal(1, adapter.DetachCallCount);
        });
    }

    private static bool GetCoreAttached(WebView control)
    {
        var property = typeof(WebViewControlRuntime).GetProperty("IsCoreAttached", BindingFlags.Instance | BindingFlags.Public);
        var field = typeof(WebView).GetField("_controlRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        Assert.NotNull(field);
        var runtime = field!.GetValue(control);
        Assert.NotNull(runtime);
        return Assert.IsType<bool>(property!.GetValue(runtime));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
