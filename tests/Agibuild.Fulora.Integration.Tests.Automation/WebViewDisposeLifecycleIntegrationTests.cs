using System;
using System.Reflection;
using Agibuild.Fulora.Testing;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Locks the behavioral contract for WebView.Dispose() across the three teardown paths:
/// (1) direct dispose with core attached, (2) dispose after early host-close detach,
/// (3) idempotent double-dispose. These tests guard against future regressions that
/// might break cleanup ordering or cause double-detach.
/// </summary>
public sealed class WebViewDisposeLifecycleIntegrationTests
{
    [AvaloniaFact]
    public void Dispose_with_attached_core_detaches_adapter_and_silences_events()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var dispatcher = new TestDispatcher();
            var adapter = MockWebViewAdapter.Create();
            var core = new WebViewCore(adapter, dispatcher);
            core.Attach(new TestPlatformHandle(IntPtr.Zero, "test-parent"));

            var control = new WebView();
            var navigationCalls = 0;
            control.NavigationStarted += (_, _) => navigationCalls++;
            control.TestOnlyAttachCore(core);
            control.TestOnlySetCoreAttached(true);
            control.TestOnlySubscribeCoreEvents();

            control.Dispose();

            Assert.Equal(1, adapter.DetachCallCount);
            Assert.False(GetCoreAttached(control));

            core.Events.RaiseNavigationStarted(
                new NavigationStartingEventArgs(Guid.NewGuid(), new Uri("https://after-dispose.test")));
            Assert.Equal(0, navigationCalls);
        });
    }

    [AvaloniaFact]
    public void Dispose_after_early_host_close_does_not_double_detach_adapter()
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
            control.TestOnlySubscribeCoreEvents();

            var detached = control.HandleHostWindowClosing(
                isProgrammatic: false,
                closeReason: unchecked((WindowCloseReason)(-1)));
            Assert.True(detached);
            Assert.Equal(1, adapter.DetachCallCount);

            var ex = Record.Exception(() => control.Dispose());
            Assert.Null(ex);
            Assert.Equal(1, adapter.DetachCallCount);
        });
    }

    [AvaloniaFact]
    public void Dispose_called_twice_does_not_throw()
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

            control.Dispose();

            var ex = Record.Exception(() => control.Dispose());
            Assert.Null(ex);
        });
    }

    [AvaloniaFact]
    public void Dispose_with_no_core_attached_does_not_throw()
    {
        AvaloniaUiThreadRunner.Run(() =>
        {
            var control = new WebView();

            var ex = Record.Exception(() => control.Dispose());
            Assert.Null(ex);
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
}
