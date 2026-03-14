namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Windows biometric provider using Windows Hello (Windows.Security.Credentials.UI).
/// Requires WinRT interop — stub until native binding is added.
/// </summary>
public sealed class WindowsBiometricProvider : IBiometricPlatformProvider
{
    /// <inheritdoc />
    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
        => Task.FromResult(new BiometricAvailability(
            OperatingSystem.IsWindows(), "windows_hello", OperatingSystem.IsWindows() ? null : "wrong_platform"));

    /// <inheritdoc />
    public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default)
        => Task.FromResult(new BiometricResult(false, "not_implemented",
            "Windows Hello integration pending — requires WinRT UserConsentVerifier API"));
}
