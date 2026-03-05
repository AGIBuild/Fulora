namespace Agibuild.Fulora.Plugin.Biometric;

/// <summary>
/// Result of a biometric authentication attempt.
/// </summary>
/// <param name="Success">Whether authentication succeeded.</param>
/// <param name="ErrorCode">Error code when failed, e.g. "user_cancelled", "not_available", "lockout", "internal_error".</param>
/// <param name="ErrorMessage">Human-readable error message when failed.</param>
public record BiometricResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);
