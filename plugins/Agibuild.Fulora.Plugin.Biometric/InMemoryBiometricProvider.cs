namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// In-memory implementation of <see cref="IBiometricPlatformProvider"/> for testing.
/// Returns configured results without platform UI.
/// </summary>
/// <param name="IsAvailable">Whether to report biometric as available.</param>
/// <param name="ShouldSucceed">Whether AuthenticateAsync should return success.</param>
/// <param name="BiometricType">The biometric type to report, e.g. "touchid", "faceid", "test".</param>
public sealed class InMemoryBiometricProvider(bool IsAvailable, bool ShouldSucceed, string BiometricType = "test")
    : IBiometricPlatformProvider
{
    /// <summary>Checks availability based on configured <see cref="IsAvailable"/>.</summary>
    public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new BiometricAvailability(
            IsAvailable,
            IsAvailable ? BiometricType : null,
            IsAvailable ? null : "not_available"));
    }

    /// <summary>Returns success or failure based on configured <see cref="ShouldSucceed"/>.</summary>
    public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return Task.FromResult(new BiometricResult(false, "not_available", "Biometric not available"));

        return Task.FromResult(ShouldSucceed
            ? new BiometricResult(true, null, null)
            : new BiometricResult(false, "user_cancelled", "User cancelled"));
    }
}
