using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.HttpClient;

/// <summary>
/// Bridge plugin manifest for the HTTP client service.
/// Register with: <c>bridge.UsePlugin&lt;HttpClientPlugin&gt;();</c>
/// </summary>
public sealed class HttpClientPlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the HttpClient plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.HttpClient",
            ["plugin.http.outbound"],
            [],
            [
                "Restrict outbound hosts with policy before enabling the plugin in production.",
                "Review default headers and interceptors because they apply to every request."
            ],
            ["desktop-hosts", "policy-governed-network-egress"]);

    /// <summary>Returns the service descriptors for the HttpClient plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IHttpClientService>(sp =>
            new HttpClientService(sp?.GetService(typeof(HttpClientOptions)) as HttpClientOptions));
    }
}
