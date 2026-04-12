using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Adapters.iOS;

internal static class iOSAdapterModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("[Agibuild.WebView] iOS adapter module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        WebViewAdapterRegistry.RegisterProvider(new iOSPlatformProvider());
    }

    [SupportedOSPlatform("ios")]
    private sealed class iOSPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "ios.wkwebview";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsIOS();
        public IWebViewAdapter CreateAdapter() => new iOSWebViewAdapter();
    }
}
