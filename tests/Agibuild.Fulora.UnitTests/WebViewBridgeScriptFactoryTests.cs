using Agibuild.Fulora.Adapters.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewBridgeScriptFactoryTests
{
    [Fact]
    public void CreateWindowsBridgeBootstrapScript_embeds_channel_and_webview_post_message()
    {
        var channelId = Guid.NewGuid();

        var script = WebViewBridgeScriptFactory.CreateWindowsBridgeBootstrapScript(channelId);

        Assert.Contains(channelId.ToString(), script, StringComparison.Ordinal);
        Assert.Contains("window.chrome.webview.postMessage", script, StringComparison.Ordinal);
        Assert.Contains("protocolVersion: 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndroidBridgeBootstrapScript_embeds_channel_and_android_bridge_shim()
    {
        var channelId = Guid.NewGuid();

        var script = WebViewBridgeScriptFactory.CreateAndroidBridgeBootstrapScript(channelId);

        Assert.Contains(channelId.ToString(), script, StringComparison.Ordinal);
        Assert.Contains("window.__agibuildBridge.postMessage(body);", script, StringComparison.Ordinal);
        Assert.Contains("window.chrome.webview.postMessage = window.__agibuildWebView.postMessage;", script, StringComparison.Ordinal);
    }
}
