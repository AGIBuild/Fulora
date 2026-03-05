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
