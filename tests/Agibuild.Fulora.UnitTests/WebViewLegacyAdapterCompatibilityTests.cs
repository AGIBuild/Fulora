using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewLegacyAdapterCompatibilityTests
{
    [Fact]
    public void GetCurrentPlatform_matches_the_legacy_platform_expected_by_current_runtime()
    {
        var platform = WebViewLegacyAdapterCompatibility.GetCurrentPlatform();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(WebViewAdapterPlatform.Windows, platform);
            return;
        }

        if (OperatingSystem.IsIOS())
        {
            Assert.Equal(WebViewAdapterPlatform.iOS, platform);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(WebViewAdapterPlatform.MacOS, platform);
            return;
        }

        if (OperatingSystem.IsAndroid())
        {
            Assert.Equal(WebViewAdapterPlatform.Android, platform);
            return;
        }

        Assert.Equal(WebViewAdapterPlatform.Gtk, platform);
    }

    [Fact]
    public void GetCurrentPlatformRegistrations_returns_only_matching_legacy_registrations()
    {
        var currentPlatform = WebViewLegacyAdapterCompatibility.GetCurrentPlatform();
        var otherPlatform = AllPlatforms.First(platform => platform != currentPlatform);
        var registrations = new[]
        {
            new WebViewAdapterRegistration(
                currentPlatform,
                "current-platform",
                () => new MockWebViewAdapter(),
                Priority: 10),
            new WebViewAdapterRegistration(
                otherPlatform,
                "other-platform",
                () => new MockWebViewAdapter(),
                Priority: 100)
        };

        var currentRegistrations = WebViewLegacyAdapterCompatibility.GetCurrentPlatformRegistrations(registrations).ToArray();

        var registration = Assert.Single(currentRegistrations);
        Assert.Equal("current-platform", registration.AdapterId);
        Assert.Equal(currentPlatform, registration.Platform);
    }

    private static readonly WebViewAdapterPlatform[] AllPlatforms =
    [
        WebViewAdapterPlatform.Windows,
        WebViewAdapterPlatform.MacOS,
        WebViewAdapterPlatform.Android,
        WebViewAdapterPlatform.Gtk,
        WebViewAdapterPlatform.iOS
    ];
}
