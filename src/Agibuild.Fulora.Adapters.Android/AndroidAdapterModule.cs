using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora.Adapters.Android;

internal static class AndroidAdapterModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal))
        {
            Console.WriteLine("[Agibuild.WebView] Android adapter module initializer running.");
            Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
        }

        WebViewAdapterRegistry.RegisterProvider(new AndroidPlatformProvider());
    }

    [SupportedOSPlatform("android")]
    private sealed class AndroidPlatformProvider : IWebViewPlatformProvider
    {
        public string Id => "android.webview";
        public int Priority => 100;
        public bool CanHandleCurrentPlatform() => OperatingSystem.IsAndroid();
        public IWebViewAdapter CreateAdapter() => new AndroidWebViewAdapter();
    }
}
