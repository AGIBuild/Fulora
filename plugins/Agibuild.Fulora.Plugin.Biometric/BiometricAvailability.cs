namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Result of checking biometric availability on the current platform.
/// </summary>
/// <param name="IsAvailable">Whether biometric authentication is available.</param>
/// <param name="BiometricType">The type of biometric hardware, e.g. "touchid", "faceid", "fingerprint", "windows_hello".</param>
/// <param name="ErrorReason">Reason when not available, e.g. "platform_not_supported", "not_configured".</param>
public record BiometricAvailability(
    bool IsAvailable,
    string? BiometricType,
    string? ErrorReason);
