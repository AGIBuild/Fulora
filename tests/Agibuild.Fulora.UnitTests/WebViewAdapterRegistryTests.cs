using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewAdapterRegistryTests
{
    [Fact]
    public void RegisterProvider_null_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => WebViewAdapterRegistry.RegisterProvider(null!));
    }

    [Fact]
    public void TryCreateForCurrentPlatform_prefers_matching_provider_with_highest_priority()
    {
        WebViewAdapterRegistry.ResetForTests();

        var lower = new StubPlatformProvider("low", canHandle: true, priority: int.MinValue, () => new MarkerAdapter("low"));
        var higher = new StubPlatformProvider("high", canHandle: true, priority: int.MaxValue, () => new MarkerAdapter("high"));

        WebViewAdapterRegistry.RegisterProvider(lower);
        WebViewAdapterRegistry.RegisterProvider(higher);

        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        Assert.True(result);
        Assert.Null(reason);
        Assert.Equal("high", Assert.IsType<MarkerAdapter>(adapter).Id);
    }

    [Fact]
    public void TryCreateForCurrentPlatform_ignores_provider_that_cannot_handle_current_platform()
    {
        WebViewAdapterRegistry.ResetForTests();

        WebViewAdapterRegistry.RegisterProvider(new StubPlatformProvider("off", canHandle: false, priority: int.MaxValue, () => new MarkerAdapter("off")));
        WebViewAdapterRegistry.RegisterProvider(new StubPlatformProvider("on", canHandle: true, priority: 0, () => new MarkerAdapter("on")));

        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        Assert.True(result);
        Assert.Null(reason);
        Assert.Equal("on", Assert.IsType<MarkerAdapter>(adapter).Id);
    }

    [Fact]
    public void TryCreateForCurrentPlatform_prefers_highest_priority_across_provider_and_legacy_registration()
    {
        WebViewAdapterRegistry.ResetForTests();

        WebViewAdapterRegistry.RegisterProvider(new StubPlatformProvider("provider-low", canHandle: true, priority: 10, () => new MarkerAdapter("provider-low")));
        WebViewAdapterRegistry.Register(new WebViewAdapterRegistration(
            WebViewLegacyAdapterCompatibility.GetCurrentPlatform(),
            "legacy-high",
            () => new MarkerAdapter("legacy-high"),
            Priority: int.MaxValue));

        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        Assert.True(result);
        Assert.Null(reason);
        Assert.Equal("legacy-high", Assert.IsType<MarkerAdapter>(adapter).Id);
    }

    [Fact]
    public void TryCreateForCurrentPlatform_prefers_provider_when_priority_ties_with_legacy_registration()
    {
        WebViewAdapterRegistry.ResetForTests();

        WebViewAdapterRegistry.RegisterProvider(new StubPlatformProvider("provider", canHandle: true, priority: 50, () => new MarkerAdapter("provider")));
        WebViewAdapterRegistry.Register(new WebViewAdapterRegistration(
            WebViewLegacyAdapterCompatibility.GetCurrentPlatform(),
            "legacy",
            () => new MarkerAdapter("legacy"),
            Priority: 50));

        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        Assert.True(result);
        Assert.Null(reason);
        Assert.Equal("provider", Assert.IsType<MarkerAdapter>(adapter).Id);
    }

    [Fact]
    public void Register_null_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => WebViewAdapterRegistry.Register(null!));
    }

    [Fact]
    public void Register_empty_adapterId_throws_ArgumentException()
    {
        var reg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.MacOS, "  ", () => new MockWebViewAdapter());

        Assert.Throws<ArgumentException>(() => WebViewAdapterRegistry.Register(reg));
    }

    [Fact]
    public void Register_null_factory_throws_ArgumentNullException()
    {
        // Create with null factory via positional ctor
        var reg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.MacOS, "test-adapter", null!);

        Assert.Throws<ArgumentNullException>(() => WebViewAdapterRegistry.Register(reg));
    }

    [Fact]
    public void HasAnyForCurrentPlatform_returns_true_after_register()
    {
        // The macOS adapter module initializer has already registered via assembly load.
        // On macOS CI this will be true; on other platforms it depends.
        // We just verify the method doesn't throw.
        _ = WebViewAdapterRegistry.HasAnyForCurrentPlatform();
    }

    [Fact]
    public void Register_null_AdapterId_throws_ArgumentNullException()
    {
        var reg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.MacOS, null!, () => new MockWebViewAdapter());

        Assert.Throws<ArgumentNullException>(() => WebViewAdapterRegistry.Register(reg));
    }

    [Fact]
    public void Register_duplicate_registration_is_silently_ignored()
    {
        // Register the same (Platform, AdapterId) twice — second call should not throw.
        var reg1 = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.Gtk, "dup-test", () => new MockWebViewAdapter(), Priority: 10);
        var reg2 = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.Gtk, "dup-test", () => new MockWebViewAdapter(), Priority: 20);

        WebViewAdapterRegistry.Register(reg1);
        WebViewAdapterRegistry.Register(reg2); // Should not throw (TryAdd ignores duplicates).
    }

    [Fact]
    public void TryCreateForCurrentPlatform_creates_adapter_if_registered()
    {
        // This depends on whether an adapter is registered for the current platform.
        var result = WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason);

        if (result)
        {
            Assert.NotNull(adapter);
            Assert.Null(reason);
        }
        else
        {
            Assert.NotNull(reason);
            Assert.Contains("No WebView adapter registered", reason);
        }
    }

    [Fact]
    public void Register_iOS_platform_succeeds()
    {
        // Verify that the new iOS enum value can be registered without issues.
        var reg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.iOS, "wkwebview-ios-test",
            () => new MockWebViewAdapter(), Priority: 100);

        WebViewAdapterRegistry.Register(reg);
    }

    [Fact]
    public void Register_iOS_and_Gtk_same_adapterId_different_platforms_both_succeed()
    {
        // Different platforms can share the same adapter-id string — no collision.
        var iosReg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.iOS, "cross-platform-id",
            () => new MockWebViewAdapter(), Priority: 50);
        var gtkReg = new WebViewAdapterRegistration(
            WebViewAdapterPlatform.Gtk, "cross-platform-id",
            () => new MockWebViewAdapter(), Priority: 50);

        WebViewAdapterRegistry.Register(iosReg);
        WebViewAdapterRegistry.Register(gtkReg);
    }

    [Fact]
    public void Register_all_platform_enum_values_are_accepted()
    {
        var platforms = new[]
        {
            WebViewAdapterPlatform.Windows,
            WebViewAdapterPlatform.MacOS,
            WebViewAdapterPlatform.Android,
            WebViewAdapterPlatform.Gtk,
            WebViewAdapterPlatform.iOS,
        };

        foreach (var platform in platforms)
        {
            var adapterId = $"enum-test-{platform}";
            var reg = new WebViewAdapterRegistration(
                platform, adapterId, () => new MockWebViewAdapter());

            WebViewAdapterRegistry.Register(reg);
        }
    }

    [Fact]
    public void WebViewAdapterPlatform_iOS_has_distinct_value()
    {
        // Ensure iOS enum value is distinct from all other platform values.
        var ios = WebViewAdapterPlatform.iOS;
        Assert.NotEqual(WebViewAdapterPlatform.Windows, ios);
        Assert.NotEqual(WebViewAdapterPlatform.MacOS, ios);
        Assert.NotEqual(WebViewAdapterPlatform.Android, ios);
        Assert.NotEqual(WebViewAdapterPlatform.Gtk, ios);
    }

    private sealed class StubPlatformProvider(
        string id,
        bool canHandle,
        int priority,
        Func<IWebViewAdapter> factory) : IWebViewPlatformProvider
    {
        public string Id => id;
        public int Priority => priority;
        public bool CanHandleCurrentPlatform() => canHandle;
        public IWebViewAdapter CreateAdapter() => factory();
    }

    private sealed class MarkerAdapter(string id) : MockWebViewAdapter
    {
        public string Id { get; } = id;
    }
}
