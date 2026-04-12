using System.Runtime.CompilerServices;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Adapters.Gtk;

internal static class GtkAdapterModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("[Agibuild.WebView] GTK adapter module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        WebViewAdapterRegistry.RegisterProvider(new GtkPlatformProvider());
    }

    private sealed class GtkPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "gtk.webkitgtk";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsLinux();
        public IWebViewAdapter CreateAdapter() => new GtkWebViewAdapter();
    }
}
