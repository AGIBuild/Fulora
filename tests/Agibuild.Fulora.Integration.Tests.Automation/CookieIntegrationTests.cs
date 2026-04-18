using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the Cookie management feature.
/// Exercises the full WebDialog → WebViewCore → ICookieAdapter (via ICookieManager) stack.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithCookies — it stores cookies in memory.
///   2. We wrap it in a WebDialog.
///   3. We get the ICookieManager from dialog.TryGetCookieManager().
///   4. We perform Set/Get/Delete/ClearAll and verify results.
/// </summary>
public sealed class CookieIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    private (WebDialog Dialog, MockWebViewAdapterWithCookies Adapter) CreateDialogWithCookies()
    {
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithCookies();
        var dialog = new WebDialog(host, adapter, _dispatcher);
        return (dialog, adapter);
    }

    // ──────────────────── Test 1: CookieManager available ────────────────────

    [AvaloniaFact]
    public void CookieManager_available_when_adapter_supports_cookies()
    {
        var (dialog, _) = CreateDialogWithCookies();

        var mgr = dialog.TryGetCookieManager();
        Assert.NotNull(mgr);
        dialog.Dispose();
    }

    // ──────────────────── Test 2: Set and Get cookie ────────────────────

    [AvaloniaFact]
    public async Task SetCookie_then_GetCookies_returns_it()
    {
        var (dialog, _) = CreateDialogWithCookies();
        var mgr = dialog.TryGetCookieManager()!;

        var cookie = new WebViewCookie("session", "abc123", ".example.com", "/", null, false, false);
        await mgr.SetCookieAsync(cookie);

        var cookies = await mgr.GetCookiesAsync(new Uri("https://example.com/page"));
        Assert.Single(cookies);
        Assert.Equal("session", cookies[0].Name);
        Assert.Equal("abc123", cookies[0].Value);
        dialog.Dispose();
    }

    // ──────────────────── Test 3: Delete cookie ────────────────────

    [AvaloniaFact]
    public async Task DeleteCookie_removes_it()
    {
        var (dialog, _) = CreateDialogWithCookies();
        var mgr = dialog.TryGetCookieManager()!;

        var cookie = new WebViewCookie("token", "xyz", ".example.com", "/", null, false, false);
        await mgr.SetCookieAsync(cookie);

        // Verify it exists
        var cookies = await mgr.GetCookiesAsync(new Uri("https://example.com"));
        Assert.Single(cookies);

        // Delete it
        await mgr.DeleteCookieAsync(cookie);

        // Verify it's gone
        cookies = await mgr.GetCookiesAsync(new Uri("https://example.com"));
        Assert.Empty(cookies);
        dialog.Dispose();
    }

    // ──────────────────── Test 4: ClearAll cookies ────────────────────

    [AvaloniaFact]
    public async Task ClearAllCookies_removes_all()
    {
        var (dialog, _) = CreateDialogWithCookies();
        var mgr = dialog.TryGetCookieManager()!;

        await mgr.SetCookieAsync(new WebViewCookie("a", "1", ".example.com", "/", null, false, false));
        await mgr.SetCookieAsync(new WebViewCookie("b", "2", ".example.com", "/", null, false, false));

        // Verify both exist
        var cookies = await mgr.GetCookiesAsync(new Uri("https://example.com"));
        Assert.Equal(2, cookies.Count);

        // Clear all
        await mgr.ClearAllCookiesAsync();

        // Verify empty
        cookies = await mgr.GetCookiesAsync(new Uri("https://example.com"));
        Assert.Empty(cookies);
        dialog.Dispose();
    }
}
