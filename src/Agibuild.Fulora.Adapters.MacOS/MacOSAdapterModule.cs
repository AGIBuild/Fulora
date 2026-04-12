using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Adapters.MacOS;

internal static class MacOSAdapterModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("[Agibuild.WebView] macOS adapter module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        WebViewAdapterRegistry.RegisterProvider(new MacOSPlatformProvider());
    }

    [SupportedOSPlatform("macos")]
    private sealed class MacOSPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "macos.wkwebview";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsMacOS();
        public IWebViewAdapter CreateAdapter() => new MacOSWebViewAdapter();
    }
}
