## Context

Fulora's plugin convention uses `IBridgePlugin` with `static abstract GetServices()` and `BridgePluginServiceDescriptor.Create<T>()`. The Auth Token plugin follows this pattern: `AuthTokenPlugin` → `IAuthTokenService` (`[JsExport]`) → `ISecureStorageProvider` (platform abstraction). Biometric auth is a distinct capability (identity verification vs token storage) that warrants a separate plugin.

**Platform APIs:**
- macOS/iOS: `LocalAuthentication` framework (`LAContext.evaluatePolicy()`)
- Windows: `Windows.Security.Credentials.UI.UserConsentVerifier.RequestVerificationAsync()`
- Android: `BiometricPrompt` (API 28+)
- Linux: No standard biometric API

## Goals / Non-Goals

**Goals:**
- `Agibuild.Fulora.Plugin.Biometric` NuGet package
- `IBiometricService` (`[JsExport]`): `IsAvailable()`, `Authenticate(reason)`, `GetBiometricType()`
- `IBiometricPlatformProvider` abstraction for platform implementations
- Platform providers: macOS, Windows, iOS, Android (Linux: not-supported stub)
- npm package `@agibuild/bridge-plugin-biometric`
- Integration example: biometric-gated token retrieval

**Non-Goals:**
- FIDO2/WebAuthn (web standard, different concern)
- Biometric enrollment management
- Continuous biometric monitoring
- Custom biometric UI

## Decisions

### D1: Separate plugin, not AuthToken extension

**Choice**: New `Agibuild.Fulora.Plugin.Biometric` plugin, independent of AuthToken.

**Rationale**: Biometric (verification) and token storage are orthogonal. Some apps need biometric without tokens. Some need tokens without biometric. Composition over coupling.

### D2: IBiometricService API surface

**Choice**:
```csharp
[JsExport]
public interface IBiometricService
{
    Task<BiometricAvailability> CheckAvailabilityAsync();
    Task<BiometricResult> AuthenticateAsync(string reason);
}
```

`BiometricAvailability`: `{ IsAvailable: bool, BiometricType: "faceId" | "touchId" | "fingerprint" | "iris" | "none", ErrorReason?: string }`
`BiometricResult`: `{ Success: bool, ErrorCode?: string, ErrorMessage?: string }`

**Rationale**: Minimal, sufficient API. Matches platform capabilities (all platforms support availability check + single authenticate call with reason string).

### D3: Multi-targeting with conditional compilation

**Choice**: Single NuGet package with multi-targeting: `net8.0` (base), `net8.0-macos` / `net8.0-ios` / `net8.0-android` / `net8.0-windows10.0.19041.0`. Platform providers registered via runtime detection.

**Rationale**: Follows .NET multi-targeting pattern. Single package for consumers. Platform code compiles only on supported targets.

### D4: Fallback behavior

**Choice**: When biometric is not available (Linux, VMs, devices without biometric hardware), `CheckAvailabilityAsync()` returns `{ IsAvailable: false, BiometricType: "none", ErrorReason: "platform_not_supported" }`. `AuthenticateAsync()` returns `{ Success: false, ErrorCode: "not_available" }`.

**Rationale**: Graceful degradation. Apps check availability before attempting auth.

## Risks / Trade-offs

- **[Risk] macOS native interop** → Need P/Invoke or native shim for `LAContext`. Consider `ObjCRuntime` bindings.
- **[Risk] Windows Hello availability** → Not available in all Windows editions. `UserConsentVerifier` returns `NotConfiguredForUser`.
- **[Trade-off] Linux not supported** → No standard biometric API on Linux. Document explicitly.

## Testing Strategy

- **CT**: `BiometricService` with `InMemoryBiometricProvider` (mock that returns configurable results)
- **CT**: `CheckAvailabilityAsync` and `AuthenticateAsync` contract scenarios
- **CT**: Fallback behavior when provider returns not-available
- **IT**: Manual validation on macOS (Touch ID) and Windows (Hello)
