using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Adapters.Windows;

internal static class WindowsAdapterModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("[Agibuild.WebView] Windows adapter module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        WebViewAdapterRegistry.RegisterProvider(new WindowsPlatformProvider());
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "windows.webview2";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsWindows();
        public IWebViewAdapter CreateAdapter() => new WindowsWebViewAdapter();
    }
}
