namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Platform abstraction for biometric operations. Implementations use platform-specific
/// APIs (Touch ID, Face ID, Windows Hello, etc.). Not exposed to JavaScript.
/// </summary>
public interface IBiometricPlatformProvider
{
    /// <summary>Checks whether biometric authentication is available.</summary>
    Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>Performs biometric authentication with the given reason.</summary>
    Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default);
}
