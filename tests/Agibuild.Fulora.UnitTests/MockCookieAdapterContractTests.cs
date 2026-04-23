using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Runs the shared <see cref="AdapterCookieContract"/> against the in-memory
/// <c>MockWebViewAdapterWithCookies</c>. This is the canonical "reference
/// implementation" pass — any platform adapter that cannot satisfy the same
/// contract against the same adapter interface is a bug in that adapter.
///
/// <para>
/// Platform-specific test projects (Windows WebView2, macOS/iOS WKWebView,
/// Linux WebKitGtk, Android WebView) are expected to add their own mirror of
/// this file that calls the same <see cref="AdapterCookieContract"/> helpers
/// with a real-adapter factory under Avalonia.Headless or a platform
/// integration host. That cross-platform parity is the concrete evidence
/// required to graduate the <c>[Experimental("AGWV001")]</c> marker on
/// <see cref="ICookieManager"/>, as tracked in
/// <c>docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md</c>.
/// </para>
/// </summary>
public sealed class MockCookieAdapterContractTests
{
    private static ICookieAdapter CreateAdapter() => MockWebViewAdapter.CreateWithCookies();

    [Fact]
    public Task Set_then_Get_returns_cookie_for_matching_host()
        => AdapterCookieContract.Set_then_Get_returns_cookie_for_matching_host(CreateAdapter);

    [Fact]
    public Task Get_on_empty_store_returns_empty_collection()
        => AdapterCookieContract.Get_on_empty_store_returns_empty_collection(CreateAdapter);

    [Fact]
    public Task Delete_removes_previously_set_cookie()
        => AdapterCookieContract.Delete_removes_previously_set_cookie(CreateAdapter);

    [Fact]
    public Task Delete_is_idempotent_for_never_set_cookie()
        => AdapterCookieContract.Delete_is_idempotent_for_never_set_cookie(CreateAdapter);

    [Fact]
    public Task Set_overwrites_cookie_with_same_name_domain_and_path()
        => AdapterCookieContract.Set_overwrites_cookie_with_same_name_domain_and_path(CreateAdapter);

    [Fact]
    public Task ClearAll_empties_the_store()
        => AdapterCookieContract.ClearAll_empties_the_store(CreateAdapter);

    [Fact]
    public Task ClearAll_is_idempotent_on_empty_store()
        => AdapterCookieContract.ClearAll_is_idempotent_on_empty_store(CreateAdapter);
}
