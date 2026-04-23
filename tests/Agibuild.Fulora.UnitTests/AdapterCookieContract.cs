using Agibuild.Fulora.Adapters.Abstractions;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Cross-platform behavior contract for any <see cref="ICookieAdapter"/>
/// implementation, expressed as a set of static helpers that take an adapter
/// factory delegate. Test classes (mock today, platform adapters tomorrow)
/// call each helper from their own <see cref="FactAttribute"/> methods.
///
/// <para>
/// The factory-delegate design avoids an inheritance hierarchy: <see cref="ICookieAdapter"/>
/// is internal (v1.x does not publish adapter authorship as a supported
/// extension point), so a public abstract test base inheriting one of its
/// methods cannot compile without violating C# accessibility rules. Using a
/// helper class keeps public test classes fully isolated from the internal
/// contract while still forcing every platform to assert identical behavior.
/// </para>
///
/// <para>
/// The intent is to make "this platform's cookie support conforms to the
/// shared semantics" a machine-checkable claim — a prerequisite for
/// graduating the <c>[Experimental("AGWV001")]</c> attribute on
/// <see cref="ICookieManager"/>.
/// </para>
///
/// <para>
/// Only behaviors that every in-box platform can realistically honor are
/// asserted. Platform nuances that are <em>not</em> a contract concern
/// (browser-level <c>HttpOnly</c> JavaScript visibility, TLS-dependent
/// <c>Secure</c> rejection paths, persistent vs. session expiry semantics)
/// belong in platform-specific test projects, not this contract.
/// </para>
///
/// <para>
/// Null-argument guarding is intentionally not part of this contract: in
/// production, every adapter call is funneled through
/// <c>RuntimeCookieManager</c> which performs <c>ArgumentNullException.ThrowIfNull</c>
/// before reaching the adapter. Pushing the same defensive check into every
/// platform adapter would duplicate code without changing any observable
/// behavior for framework consumers. If a future change exposes
/// <see cref="ICookieAdapter"/> publicly, argument-null tests move here in the
/// same commit that makes the interface public.
/// </para>
/// </summary>
internal static class AdapterCookieContract
{
    internal delegate ICookieAdapter AdapterFactory();

    private static WebViewCookie MakeCookie(
        string name = "sid",
        string value = "abc123",
        string domain = ".example.com",
        string path = "/",
        bool secure = false,
        bool httpOnly = false)
        => new(name, value, domain, path, Expires: null, IsSecure: secure, IsHttpOnly: httpOnly);

    public static async Task Set_then_Get_returns_cookie_for_matching_host(AdapterFactory factory)
    {
        var adapter = factory();
        var cookie = MakeCookie();

        await adapter.SetCookieAsync(cookie);
        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));

        Assert.Contains(cookies, c => c.Name == "sid" && c.Value == "abc123");
    }

    public static async Task Get_on_empty_store_returns_empty_collection(AdapterFactory factory)
    {
        var adapter = factory();

        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));

        Assert.NotNull(cookies);
        Assert.Empty(cookies);
    }

    public static async Task Delete_removes_previously_set_cookie(AdapterFactory factory)
    {
        var adapter = factory();
        var cookie = MakeCookie();

        await adapter.SetCookieAsync(cookie);
        await adapter.DeleteCookieAsync(cookie);

        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.DoesNotContain(cookies, c => c.Name == "sid");
    }

    public static async Task Delete_is_idempotent_for_never_set_cookie(AdapterFactory factory)
    {
        var adapter = factory();

        // Deleting a cookie that was never set must not throw. Platform adapters
        // that have no native "delete" API (e.g. Android) emulate deletion by
        // setting an expired cookie; that emulation must also succeed here.
        await adapter.DeleteCookieAsync(MakeCookie(name: "missing"));
    }

    public static async Task Set_overwrites_cookie_with_same_name_domain_and_path(AdapterFactory factory)
    {
        var adapter = factory();

        await adapter.SetCookieAsync(MakeCookie(value: "v1"));
        await adapter.SetCookieAsync(MakeCookie(value: "v2"));

        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));
        var sid = Assert.Single(cookies, c => c.Name == "sid");
        Assert.Equal("v2", sid.Value);
    }

    public static async Task ClearAll_empties_the_store(AdapterFactory factory)
    {
        var adapter = factory();
        await adapter.SetCookieAsync(MakeCookie(name: "a"));
        await adapter.SetCookieAsync(MakeCookie(name: "b"));

        await adapter.ClearAllCookiesAsync();

        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);
    }

    public static async Task ClearAll_is_idempotent_on_empty_store(AdapterFactory factory)
    {
        var adapter = factory();

        await adapter.ClearAllCookiesAsync();
        await adapter.ClearAllCookiesAsync();

        var cookies = await adapter.GetCookiesAsync(new Uri("https://example.com/"));
        Assert.Empty(cookies);
    }
}
