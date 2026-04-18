using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class DevToolsTests
{
    [Fact]
    public void IWebView_declares_async_DevTools_members()
    {
        Assert.True(typeof(IWebViewDevTools).IsAssignableFrom(typeof(IWebView)));

        var methods = typeof(IWebViewDevTools).GetMethod("OpenDevToolsAsync");
        Assert.NotNull(methods);

        methods = typeof(IWebViewDevTools).GetMethod("CloseDevToolsAsync");
        Assert.NotNull(methods);

        methods = typeof(IWebViewDevTools).GetMethod("IsDevToolsOpenAsync");
        Assert.NotNull(methods);
        Assert.Equal(typeof(Task<bool>), methods!.ReturnType);
    }

    [Fact]
    public void IDevToolsAdapter_interface_has_expected_members()
    {
        Assert.True(typeof(IWebViewDevTools).IsAssignableFrom(typeof(IWebView)));
        Assert.NotNull(typeof(IWebViewDevTools).GetMethod("OpenDevToolsAsync"));
        Assert.NotNull(typeof(IWebViewDevTools).GetMethod("CloseDevToolsAsync"));
        Assert.NotNull(typeof(IWebViewDevTools).GetMethod("IsDevToolsOpenAsync"));
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
