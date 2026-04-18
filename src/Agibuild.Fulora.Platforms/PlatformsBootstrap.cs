using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Platforms;

// Unified bootstrap for the three net10.0 desktop platform adapters
// (Windows/WebView2, macOS/WKWebView, Linux/WebKitGTK). A single
// ModuleInitializer replaces the three per-adapter-project initializers
// that previously lived in Agibuild.Fulora.Adapters.{Windows,MacOS,Gtk}.
//
// Runtime platform dispatch is still performed by each provider's
// CanHandleCurrentPlatform() — registration is eager and idempotent,
// resolution happens at WebView creation time via WebViewAdapterRegistry.
internal static class PlatformsBootstrap
{
    private static bool DiagnosticsEnabled
        => string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal);

    [ModuleInitializer]
    internal static void Register()
    {
        if (DiagnosticsEnabled)
        {
            Console.WriteLine("[Agibuild.WebView] Platforms bootstrap module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        // Register only the provider that matches the current OS. This both satisfies
        // platform-compatibility analysis (CA1416) for the platform-guarded provider
        // types below, and avoids polluting the registry with providers whose
        // CreateAdapter() would fail if ever invoked on a foreign platform.
        if (OperatingSystem.IsWindows())
        {
            WebViewAdapterRegistry.RegisterProvider(new WindowsPlatformProvider());
        }
        else if (OperatingSystem.IsMacOS())
        {
            WebViewAdapterRegistry.RegisterProvider(new MacOSPlatformProvider());
        }
        else if (OperatingSystem.IsLinux())
        {
            WebViewAdapterRegistry.RegisterProvider(new GtkPlatformProvider());
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "windows.webview2";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsWindows();
        public IWebViewAdapter CreateAdapter() => new Agibuild.Fulora.Adapters.Windows.WindowsWebViewAdapter();
    }

    [SupportedOSPlatform("macos")]
    private sealed class MacOSPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "macos.wkwebview";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsMacOS();
        public IWebViewAdapter CreateAdapter() => new Agibuild.Fulora.Adapters.MacOS.MacOSWebViewAdapter();
    }

    private sealed class GtkPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "gtk.webkitgtk";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsLinux();
        public IWebViewAdapter CreateAdapter() => new Agibuild.Fulora.Adapters.Gtk.GtkWebViewAdapter();
    }
}
