using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class DevToolsTests
{
    [Fact]
    public void IWebView_declares_async_DevTools_members()
    {
        Assert.True(typeof(IWebViewFeatures).IsAssignableFrom(typeof(IWebView)));

        var methods = typeof(IWebViewFeatures).GetMethod("OpenDevToolsAsync");
        Assert.NotNull(methods);

        methods = typeof(IWebViewFeatures).GetMethod("CloseDevToolsAsync");
        Assert.NotNull(methods);

        methods = typeof(IWebViewFeatures).GetMethod("IsDevToolsOpenAsync");
        Assert.NotNull(methods);
        Assert.Equal(typeof(Task<bool>), methods!.ReturnType);
    }

    [Fact]
    public void IDevToolsAdapter_interface_has_expected_members()
    {
        Assert.True(typeof(IWebViewFeatures).IsAssignableFrom(typeof(IWebView)));
        Assert.NotNull(typeof(IWebViewFeatures).GetMethod("OpenDevToolsAsync"));
        Assert.NotNull(typeof(IWebViewFeatures).GetMethod("CloseDevToolsAsync"));
        Assert.NotNull(typeof(IWebViewFeatures).GetMethod("IsDevToolsOpenAsync"));
    }

    [Fact]
    public async Task TestWebViewHost_DevTools_are_noop()
    {
        using var host = new Agibuild.Fulora.Testing.TestWebViewHost();
        await host.OpenDevToolsAsync();
        await host.CloseDevToolsAsync();
        Assert.False(await host.IsDevToolsOpenAsync());
    }
}
