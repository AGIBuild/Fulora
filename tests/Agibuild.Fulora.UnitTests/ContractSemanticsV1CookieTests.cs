using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class ContractSemanticsV1CookieTests
{
    [Fact]
    public void TryGetCookieManager_returns_non_null_when_adapter_supports_ICookieAdapter()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var webView = new WebViewCore(adapter, dispatcher);

        var cm = webView.TryGetCookieManager();
        Assert.NotNull(cm);
    }

    [Fact]
    public async Task Cookie_CRUD_set_get_delete_clear()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var webView = new WebViewCore(adapter, dispatcher);
        var cm = webView.TryGetCookieManager()!;

        var cookie = new WebViewCookie("sid", "abc123", ".example.com", "/", null, false, false);

        // Set
        await cm.SetCookieAsync(cookie);

        // Get
        var cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Single(cookies);
        Assert.Equal("sid", cookies[0].Name);
        Assert.Equal("abc123", cookies[0].Value);

        // Delete
        await cm.DeleteCookieAsync(cookie);
        cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);

        // Set again and clear all
        await cm.SetCookieAsync(cookie);
        await cm.SetCookieAsync(new WebViewCookie("token", "xyz", ".example.com", "/", null, true, true));
        await cm.ClearAllCookiesAsync();
        cookies = await cm.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);
    }

    [Fact]
    public async Task Cookie_operation_after_dispose_throws_ObjectDisposedException()
    {
        var dispatcher = new TestDispatcher();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var webView = new WebViewCore(adapter, dispatcher);
        var cm = webView.TryGetCookieManager()!;

        webView.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => cm.GetCookiesAsync(new Uri("https://example.com/")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cm.SetCookieAsync(
            new WebViewCookie("a", "b", ".example.com", "/", null, false, false)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cm.DeleteCookieAsync(
            new WebViewCookie("a", "b", ".example.com", "/", null, false, false)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => cm.ClearAllCookiesAsync());
    }
}
