using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.AuthToken;

/// <summary>
/// Bridge plugin manifest for the AuthToken service.
/// Register with: <c>bridge.UsePlugin&lt;AuthTokenPlugin&gt;();</c>
/// Uses <see cref="InMemorySecureStorageProvider"/> as the default storage.
/// </summary>
public sealed class AuthTokenPlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the AuthToken plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.AuthToken",
            ["plugin.auth.token.read", "plugin.auth.token.write"],
            [],
            [
                "The default in-memory provider is test-only and does not satisfy production secure storage requirements.",
                "Tokens should be scoped and rotated according to the host application's security policy."
            ],
            ["desktop-hosts", "secure-storage-provider-required-for-production"]);

    /// <summary>Returns service descriptors for the AuthToken plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IAuthTokenService>(
            _ => new AuthTokenService(new InMemorySecureStorageProvider()));
    }
}
