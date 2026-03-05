namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Implementation of <see cref="IBiometricService"/> that delegates to
/// <see cref="IBiometricPlatformProvider"/>. Wraps provider exceptions in
/// <see cref="BiometricResult"/> with ErrorCode "internal_error".
/// </summary>
public sealed class BiometricService(IBiometricPlatformProvider provider) : IBiometricService
{
    private readonly IBiometricPlatformProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public async Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            return await _provider.CheckAvailabilityAsync(ct);
        }
        catch (Exception ex)
        {
            return new BiometricAvailability(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        try
        {
            return await _provider.AuthenticateAsync(reason, ct);
        }
        catch (Exception ex)
        {
            return new BiometricResult(false, "internal_error", ex.Message);
        }
    }
}
