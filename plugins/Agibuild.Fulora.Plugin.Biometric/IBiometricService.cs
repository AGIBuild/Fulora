using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Bridge service for biometric availability check and authentication.
/// </summary>
[JsExport]
public interface IBiometricService
{
    /// <summary>Checks whether biometric authentication is available on the current platform.</summary>
    Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>Performs biometric authentication with the given reason shown to the user.</summary>
    Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default);
}
