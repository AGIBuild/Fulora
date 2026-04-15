using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewOverlayRuntimeTests
{
    [Fact]
    public void UpdateOverlayContent_creates_host_sets_content_and_refreshes_when_visual_root_exists()
    {
        var webView = new WebView();
        var refreshCalls = 0;
        var runtime = CreateRuntime(
            webView,
            hasVisualRoot: () => true,
            refreshOverlayLayout: () => refreshCalls++);

        var content = new object();
        runtime.UpdateOverlayContent(content);

        Assert.NotNull(runtime.OverlayHost);
        Assert.Same(content, runtime.OverlayHost!.Content);
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public void UpdateOverlayContent_null_disposes_existing_host()
    {
        var webView = new WebView();
        var runtime = CreateRuntime(webView);
        runtime.UpdateOverlayContent(new object());

        runtime.UpdateOverlayContent(null);

        Assert.Null(runtime.OverlayHost);
    }

    [Fact]
    public void AttachVisualHooks_subscribes_layout_and_tracks_window_switches()
    {
        var webView = new WebView();
        var firstWindow = new object();
        var secondWindow = new object();
        object? currentWindow = firstWindow;
        EventHandler? layoutUpdatedHandler = null;
        var subscribedWindows = new List<object>();
        var unsubscribedWindows = new List<object>();

        var runtime = CreateRuntime(
            webView,
            getTopLevelWindow: () => currentWindow,
            subscribeLayoutUpdated: handler => layoutUpdatedHandler = handler,
            unsubscribeLayoutUpdated: handler =>
            {
                if (ReferenceEquals(layoutUpdatedHandler, handler))
                    layoutUpdatedHandler = null;
            },
            subscribeWindowPositionChanged: (window, _) => subscribedWindows.Add(window),
            unsubscribeWindowPositionChanged: (window, _) => unsubscribedWindows.Add(window));

        runtime.AttachVisualHooks();
        runtime.AttachVisualHooks();

        currentWindow = secondWindow;
        layoutUpdatedHandler!.Invoke(this, EventArgs.Empty);
        runtime.DetachVisualHooks();

        Assert.Equal([firstWindow, secondWindow], subscribedWindows);
        Assert.Equal([firstWindow, secondWindow], unsubscribedWindows);
        Assert.Null(layoutUpdatedHandler);
    }

    [Fact]
    public void Dispose_detaches_hooks_and_disposes_overlay_host()
    {
        var webView = new WebView();
        var window = new object();
        EventHandler? layoutUpdatedHandler = null;
        var unsubscribed = false;

        var runtime = CreateRuntime(
            webView,
            getTopLevelWindow: () => window,
            subscribeLayoutUpdated: handler => layoutUpdatedHandler = handler,
            unsubscribeLayoutUpdated: handler =>
            {
                if (ReferenceEquals(layoutUpdatedHandler, handler))
                    layoutUpdatedHandler = null;
            },
            unsubscribeWindowPositionChanged: (_, _) => unsubscribed = true);
        runtime.UpdateOverlayContent(new object());
        runtime.AttachVisualHooks();

        runtime.Dispose();

        Assert.Null(runtime.OverlayHost);
        Assert.Null(layoutUpdatedHandler);
        Assert.True(unsubscribed);
    }

    private static WebViewOverlayRuntime CreateRuntime(
        WebView webView,
        Func<bool>? hasVisualRoot = null,
        Func<object?>? getTopLevelWindow = null,
        Action<EventHandler>? subscribeLayoutUpdated = null,
        Action<EventHandler>? unsubscribeLayoutUpdated = null,
        Action<object, EventHandler<PixelPointEventArgs>>? subscribeWindowPositionChanged = null,
        Action<object, EventHandler<PixelPointEventArgs>>? unsubscribeWindowPositionChanged = null,
        Action? refreshOverlayLayout = null)
    {
        return new WebViewOverlayRuntime(
            createOverlayHost: () => new WebViewOverlayHost(webView),
            hasVisualRoot: hasVisualRoot ?? (() => false),
            getTopLevelWindow: getTopLevelWindow ?? (() => null),
            subscribeLayoutUpdated: subscribeLayoutUpdated ?? (_ => { }),
            unsubscribeLayoutUpdated: unsubscribeLayoutUpdated ?? (_ => { }),
            subscribeWindowPositionChanged: subscribeWindowPositionChanged ?? ((_, _) => { }),
            unsubscribeWindowPositionChanged: unsubscribeWindowPositionChanged ?? ((_, _) => { }),
            refreshOverlayLayout: refreshOverlayLayout ?? (() => { }));
    }
}
