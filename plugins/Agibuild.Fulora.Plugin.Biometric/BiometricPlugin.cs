using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Bridge plugin manifest for the Biometric service.
/// Register with: <c>bridge.UsePlugin&lt;BiometricPlugin&gt;();</c>
/// Uses <see cref="InMemoryBiometricProvider"/> as the default provider.
/// Register <see cref="IBiometricPlatformProvider"/> via DI to override.
/// </summary>
public sealed class BiometricPlugin : IBridgePlugin
{
    /// <summary>Returns policy metadata for the Biometric plugin.</summary>
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Agibuild.Fulora.Plugin.Biometric",
            ["plugin.biometric.prompt"],
            [],
            [
                "Biometric prompts require explicit user presence and inherit the host platform's trust boundary.",
                "Replace the in-memory provider with a platform provider before shipping real authentication flows."
            ],
            ["windows", "macos", "ios", "android"]);

    /// <summary>Returns service descriptors for the Biometric plugin.</summary>
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IBiometricService>(sp =>
        {
            var provider = sp?.GetService(typeof(IBiometricPlatformProvider)) as IBiometricPlatformProvider;
            return new BiometricService(provider ?? new InMemoryBiometricProvider(true, true));
        });
    }
}
