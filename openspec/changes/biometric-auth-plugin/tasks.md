# Biometric Auth Plugin — Tasks

## 1. Core Contracts

- [x] 1.1 Create `plugins/Agibuild.Fulora.Plugin.Biometric/` project (net10.0)
- [x] 1.2 Define `IBiometricService` (`[JsExport]`): `CheckAvailabilityAsync()`, `AuthenticateAsync(string reason)`
- [x] 1.3 Define `BiometricAvailability` record: `IsAvailable`, `BiometricType`, `ErrorReason?`
- [x] 1.4 Define `BiometricResult` record: `Success`, `ErrorCode?`, `ErrorMessage?`
- [x] 1.5 Define `IBiometricPlatformProvider` interface: `CheckAvailabilityAsync()`, `AuthenticateAsync(reason)`
- [x] 1.6 Create `BiometricPlugin : IBridgePlugin` with `GetServices()` registration

## 2. Platform Providers

- [x] 2.1 Implement `InMemoryBiometricProvider` (testing/fallback — configurable success/failure)
- [ ] 2.2 Implement macOS provider using `LAContext` via P/Invoke or ObjCRuntime
- [ ] 2.3 Implement iOS provider (same as macOS — `LocalAuthentication` framework)
- [ ] 2.4 Implement Windows provider using `UserConsentVerifier.RequestVerificationAsync()`
- [ ] 2.5 Implement Android provider using `BiometricPrompt` API
- [ ] 2.6 Implement Linux stub: returns `{ IsAvailable: false, ErrorReason: "platform_not_supported" }`

## 3. Service Implementation

- [x] 3.1 Implement `BiometricService : IBiometricService` delegating to `IBiometricPlatformProvider`
- [x] 3.2 Handle provider exceptions → `BiometricResult { Success: false, ErrorCode: "internal_error" }`

## 4. npm Package

- [ ] 4.1 Create `packages/bridge-plugin-biometric/` npm package
- [ ] 4.2 Define TypeScript types: `BiometricAvailability`, `BiometricResult`, `IBiometricService`
- [ ] 4.3 Add `package.json` with appropriate metadata and peer dependency on `@agibuild/bridge`

## 5. Tests

- [x] 5.1 CT: `BiometricService` with `InMemoryBiometricProvider` — available + success
- [x] 5.2 CT: `BiometricService` with `InMemoryBiometricProvider` — available + user_cancelled
- [x] 5.3 CT: `BiometricService` with `InMemoryBiometricProvider` — not available
- [x] 5.4 CT: `BiometricPlugin` registers service correctly
- [x] 5.5 CT: `BiometricAvailability` and `BiometricResult` construction
- [x] 5.6 CT: `BiometricService` wraps provider exception as internal_error
- [ ] 5.7 Manual IT: macOS Touch ID prompt appears and returns correct result
- [ ] 5.8 Manual IT: Windows Hello prompt appears and returns correct result

## 6. Documentation

- [ ] 6.1 Add biometric plugin to plugin authoring guide examples
- [ ] 6.2 Document integration pattern: biometric-gated token retrieval
- [ ] 6.3 Add platform support matrix (macOS ✅, Windows ✅, iOS ✅, Android ✅, Linux ❌)
