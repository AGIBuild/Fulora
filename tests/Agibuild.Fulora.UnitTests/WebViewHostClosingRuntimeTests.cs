using Avalonia.Controls;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewHostClosingRuntimeTests
{
    [Fact]
    public void RefreshHook_subscribes_to_current_window_and_moves_subscription_when_window_changes()
    {
        var firstWindow = new object();
        var secondWindow = new object();
        object? currentWindow = firstWindow;
        var subscribed = new List<object>();
        var unsubscribed = new List<object>();

        var runtime = new WebViewHostClosingRuntime(
            resolveHostWindow: () => currentWindow,
            subscribe: (window, _) =>
            {
                subscribed.Add(window);
                return $"token-{subscribed.Count}";
            },
            unsubscribe: (window, _) => unsubscribed.Add(window),
            isCoreAttached: () => true,
            detachForClosing: (_, _) => true);

        runtime.RefreshHook();
        runtime.RefreshHook();

        currentWindow = secondWindow;
        runtime.RefreshHook();
        runtime.Unhook();

        Assert.Equal([firstWindow, secondWindow], subscribed);
        Assert.Equal([firstWindow, secondWindow], unsubscribed);
    }

    [Fact]
    public void HandleHostWindowClosing_returns_false_when_core_is_not_attached()
    {
        var detachCalls = 0;
        var runtime = new WebViewHostClosingRuntime(
            resolveHostWindow: static () => null,
            subscribe: static (_, _) => null,
            unsubscribe: static (_, _) => { },
            isCoreAttached: () => false,
            detachForClosing: (_, _) =>
            {
                detachCalls++;
                return true;
            });

        var detached = runtime.HandleHostWindowClosing(false, WindowCloseReason.WindowClosing);

        Assert.False(detached);
        Assert.Equal(0, detachCalls);
    }

    [Fact]
    public void HandleHostWindowClosing_detaches_when_core_is_attached()
    {
        var detachCalls = 0;
        var runtime = new WebViewHostClosingRuntime(
            resolveHostWindow: static () => null,
            subscribe: static (_, _) => null,
            unsubscribe: static (_, _) => { },
            isCoreAttached: () => true,
            detachForClosing: (_, _) =>
            {
                detachCalls++;
                return true;
            });

        var detached = runtime.HandleHostWindowClosing(true, WindowCloseReason.WindowClosing);

        Assert.True(detached);
        Assert.Equal(1, detachCalls);
    }

    [Fact]
    public void HandleHostWindowClosing_swallows_detach_failures()
    {
        var runtime = new WebViewHostClosingRuntime(
            resolveHostWindow: static () => null,
            subscribe: static (_, _) => null,
            unsubscribe: static (_, _) => { },
            isCoreAttached: () => true,
            detachForClosing: (_, _) => throw new InvalidOperationException("detach failed"));

        var detached = runtime.HandleHostWindowClosing(false, WindowCloseReason.WindowClosing);

        Assert.False(detached);
    }
}
