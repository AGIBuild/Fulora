using Agibuild.Fulora;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class OverlayHostTests
{
    [Fact]
    public void WebViewOverlayHost_construction()
    {
        var webView = new WebView();
        var host = new WebViewOverlayHost(webView);

        Assert.NotNull(host);
    }

    [Fact]
    public void WebViewOverlayHost_Content_get_set()
    {
        var webView = new WebView();
        var host = new WebViewOverlayHost(webView);

        Assert.Null(host.Content);

        var content = new object();
        host.Content = content;
        Assert.Same(content, host.Content);

        host.Content = null;
        Assert.Null(host.Content);
    }

    [Fact]
    public void WebViewOverlayHost_Dispose_cleans_up()
    {
        var webView = new WebView();
        var host = new WebViewOverlayHost(webView);
        host.Content = new object();

        host.Dispose();

        Assert.Null(host.Content);
        Assert.False(host.IsVisible);

        // Double dispose is safe
        host.Dispose();
    }
}
