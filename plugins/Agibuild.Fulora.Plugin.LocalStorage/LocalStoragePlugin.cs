using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.LocalStorage;

/// <summary>
/// Bridge plugin manifest for the LocalStorage service.
/// Register with: <c>bridge.UsePlugin&lt;LocalStoragePlugin&gt;();</c>
/// </summary>
public sealed class LocalStoragePlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the LocalStorage plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.LocalStorage",
            ["plugin.localstorage.read", "plugin.localstorage.write"],
            [],
            [
                "Local storage persists application data on disk and should not be used for secrets.",
                "Hosts should keep storage paths inside the application profile or another governed directory."
            ],
            ["desktop-hosts", "app-profile-scope"]);

    /// <summary>Returns service descriptors for the LocalStorage plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<ILocalStorageService>(
            _ => new LocalStorageService());
    }
}
