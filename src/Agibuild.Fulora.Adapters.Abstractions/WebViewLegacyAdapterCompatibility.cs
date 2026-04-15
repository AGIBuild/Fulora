namespace Agibuild.Fulora.Adapters.Abstractions;

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
