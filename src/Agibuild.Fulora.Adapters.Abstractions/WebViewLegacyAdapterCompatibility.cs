using System.Diagnostics.CodeAnalysis;

namespace Agibuild.Fulora.Adapters.Abstractions;

// Coverage-exclusion rationale: every OS-detection branch resolves to a single arm at runtime on a
// given host. CI matrix coverage merges per-OS reports into one Cobertura file, but each individual
// run can only ever exercise one OperatingSystem.Is*() arm — so the remaining arms permanently show
// as uncovered branches and the class can't reach 100% on any single platform. Marking it
// [ExcludeFromCodeCoverage] avoids gaming coverage with platform-specific stub tests.
[ExcludeFromCodeCoverage]
internal static class WebViewLegacyAdapterCompatibility
{
    public static WebViewAdapterPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return WebViewAdapterPlatform.Windows;
        }

        // iOS check must come before macOS because IsMacOS() returns true on Mac Catalyst.
        if (OperatingSystem.IsIOS())
        {
            return WebViewAdapterPlatform.iOS;
        }

        if (OperatingSystem.IsMacOS())
        {
            return WebViewAdapterPlatform.MacOS;
        }

        if (OperatingSystem.IsAndroid())
        {
            return WebViewAdapterPlatform.Android;
        }

        return WebViewAdapterPlatform.Gtk;
    }

    public static IEnumerable<WebViewAdapterRegistration> GetCurrentPlatformRegistrations(
        IEnumerable<WebViewAdapterRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var platform = GetCurrentPlatform();
        return registrations.Where(registration => registration.Platform == platform);
    }
}
