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
            SetCoreAttached(control, value: true);

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

    private static void SetCoreAttached(WebView control, bool value)
    {
        var field = typeof(WebView).GetField("_coreAttached", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(control, value);
    }

    private static bool GetCoreAttached(WebView control)
    {
        var field = typeof(WebView).GetField("_coreAttached", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<bool>(field!.GetValue(control));
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }
}
