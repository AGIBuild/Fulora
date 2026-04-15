using Avalonia.Platform;
using Agibuild.Fulora.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlLifecycleRuntimeTests
{
    private readonly TestDispatcher _dispatcher = new();

    [Fact]
    public void AttachToNativeControl_creates_core_attaches_and_replays_pending_source()
    {
        var adapter = MockWebViewAdapter.Create();
        var controlRuntime = new WebViewControlRuntime();
        var eventRuntime = new WebViewControlEventRuntime(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, _ => { },
            () => null, () => null, () => null, () => null, () => null,
            _ => Task.CompletedTask, () => 1.0, _ => { });

        var lifecycle = new WebViewControlLifecycleRuntime(
            controlRuntime,
            eventRuntime,
            getLoggerFactory: () => NullLoggerFactory.Instance,
            getEnvironmentOptions: () => null,
            getPendingSource: () => new Uri("https://example.test/replayed"),
            setCoreAttached: _ => { },
            createDispatcher: () => _dispatcher,
            createCore: (_, _, _) => new WebViewCore(adapter, _dispatcher),
            wrapPlatformHandle: handle => new TestNativeHandle(handle.Handle, handle.HandleDescriptor ?? string.Empty));

        lifecycle.AttachToNativeControl(new TestAvaloniaPlatformHandle(IntPtr.Zero, "test-parent"));

        Assert.NotNull(controlRuntime.Core);
        Assert.Equal(1, adapter.AttachCallCount);
        Assert.Equal(new Uri("https://example.test/replayed"), adapter.LastNavigationUri);
    }

    [Fact]
    public void AttachToNativeControl_success_does_not_depend_on_a_shell_adapter_callback()
    {
        var adapter = MockWebViewAdapter.Create();
        var controlRuntime = new WebViewControlRuntime();
        var eventRuntime = new WebViewControlEventRuntime(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, _ => { },
            () => null, () => null, () => null, () => null, () => null,
            _ => Task.CompletedTask, () => 1.0, _ => { });

        var lifecycle = new WebViewControlLifecycleRuntime(
            controlRuntime,
            eventRuntime,
            getLoggerFactory: () => NullLoggerFactory.Instance,
            getEnvironmentOptions: () => null,
            getPendingSource: () => null,
            setCoreAttached: attached => controlRuntime.SetCoreAttached(attached),
            createDispatcher: () => _dispatcher,
            createCore: (_, _, _) => new WebViewCore(adapter, _dispatcher),
            wrapPlatformHandle: handle => new TestNativeHandle(handle.Handle, handle.HandleDescriptor ?? string.Empty));

        lifecycle.AttachToNativeControl(new TestAvaloniaPlatformHandle(IntPtr.Zero, "test-parent"));

        Assert.NotNull(controlRuntime.Core);
        Assert.Equal(1, adapter.AttachCallCount);
        Assert.True(controlRuntime.IsCoreAttached);
    }

    [Fact]
    public void AttachToNativeControl_platform_not_supported_marks_runtime_unavailable_without_a_shell_adapter_callback()
    {
        var controlRuntime = new WebViewControlRuntime();
        var eventRuntime = new WebViewControlEventRuntime(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, _ => { },
            () => null, () => null, () => null, () => null, () => null,
            _ => Task.CompletedTask, () => 1.0, _ => { });

        var lifecycle = new WebViewControlLifecycleRuntime(
            controlRuntime,
            eventRuntime,
            () => NullLoggerFactory.Instance,
            () => null,
            () => null,
            attached => controlRuntime.SetCoreAttached(attached),
            () => _dispatcher,
            createCore: (_, _, _) => throw new PlatformNotSupportedException());

        lifecycle.AttachToNativeControl(new TestAvaloniaPlatformHandle(IntPtr.Zero, "test-parent"));

        Assert.Null(controlRuntime.Core);
        Assert.False(controlRuntime.IsCoreAttached);
        Assert.Throws<PlatformNotSupportedException>(() => { _ = controlRuntime.Bridge; });
    }

    [Fact]
    public void AttachToNativeControl_rethrows_non_platform_failures_and_clears_runtime_state()
    {
        var controlRuntime = new WebViewControlRuntime();
        var eventRuntime = new WebViewControlEventRuntime(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, _ => { },
            () => null, () => null, () => null, () => null, () => null,
            _ => Task.CompletedTask, () => 1.0, _ => { });
        var lifecycle = new WebViewControlLifecycleRuntime(
            controlRuntime,
            eventRuntime,
            () => NullLoggerFactory.Instance,
            () => null,
            () => null,
            _ => { },
            () => _dispatcher,
            createCore: (_, _, _) => throw new InvalidOperationException("boom"));

        var error = Assert.Throws<InvalidOperationException>(
            () => lifecycle.AttachToNativeControl(new TestAvaloniaPlatformHandle(IntPtr.Zero, "test-parent")));

        Assert.Equal("boom", error.Message);
        Assert.Null(controlRuntime.Core);
        Assert.Throws<InvalidOperationException>(() => { _ = controlRuntime.Bridge; });
    }

    [Fact]
    public void DestroyAttachedCore_detaches_events_and_disposes_core()
    {
        var adapter = MockWebViewAdapter.Create();
        var controlRuntime = new WebViewControlRuntime();
        var eventRuntime = new WebViewControlEventRuntime(
            _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, _ => { },
            () => null, () => null, () => null, () => null, () => null,
            _ => Task.CompletedTask, () => 1.0, _ => { });
        var lifecycle = new WebViewControlLifecycleRuntime(
            controlRuntime, eventRuntime,
            () => NullLoggerFactory.Instance, () => null, () => null, _ => { }, () => _dispatcher,
            createCore: (_, _, _) => new WebViewCore(adapter, _dispatcher),
            wrapPlatformHandle: handle => new TestNativeHandle(handle.Handle, handle.HandleDescriptor ?? string.Empty));
        var core = new WebViewCore(adapter, _dispatcher);
        controlRuntime.AttachCore(core);
        controlRuntime.SetCoreAttached(true);
        eventRuntime.Attach(core);
        core.Attach(new TestNativeHandle(IntPtr.Zero, "test-parent"));

        lifecycle.DestroyAttachedCore();

        Assert.Equal(1, adapter.DetachCallCount);
        Assert.Null(controlRuntime.Core);
        Assert.False(controlRuntime.IsCoreAttached);
    }

    private sealed class TestNativeHandle(nint handle, string descriptor) : INativeHandle
    {
        public nint Handle => handle;
        public string HandleDescriptor => descriptor;
    }

    private sealed class TestAvaloniaPlatformHandle(nint handle, string descriptor) : IPlatformHandle
    {
        public nint Handle => handle;
        public string HandleDescriptor => descriptor;
    }
}
