## Purpose

Define requirements for the biometric authentication plugin. Ensures cross-platform biometric verification is available via the bridge with graceful degradation.

## ADDED Requirements

### Requirement: IBiometricService exposes availability check and authentication

`IBiometricService` (`[JsExport]`) SHALL provide biometric availability and authentication methods.

#### Scenario: Biometric available on macOS with Touch ID
- **GIVEN** a macOS device with Touch ID hardware
- **WHEN** `CheckAvailabilityAsync()` is called
- **THEN** it SHALL return `{ IsAvailable: true, BiometricType: "touchId" }`

#### Scenario: Biometric available on Windows with Windows Hello
- **GIVEN** a Windows device with Windows Hello configured
- **WHEN** `CheckAvailabilityAsync()` is called
- **THEN** it SHALL return `{ IsAvailable: true, BiometricType: "faceId" }` or `{ BiometricType: "fingerprint" }` depending on hardware

#### Scenario: Biometric not available on Linux
- **GIVEN** a Linux system
- **WHEN** `CheckAvailabilityAsync()` is called
- **THEN** it SHALL return `{ IsAvailable: false, BiometricType: "none", ErrorReason: "platform_not_supported" }`

#### Scenario: Biometric not configured on Windows
- **GIVEN** a Windows device without Windows Hello configured
- **WHEN** `CheckAvailabilityAsync()` is called
- **THEN** it SHALL return `{ IsAvailable: false, BiometricType: "none", ErrorReason: "not_configured" }`

### Requirement: AuthenticateAsync performs biometric verification

`AuthenticateAsync` SHALL invoke platform biometric verification and return structured success or failure results.

#### Scenario: Successful authentication
- **GIVEN** biometric is available and the user has enrolled biometrics
- **WHEN** `AuthenticateAsync("Confirm your identity to access secrets")` is called
- **THEN** the platform biometric prompt SHALL be displayed with the reason string
- **AND** upon successful biometric scan, it SHALL return `{ Success: true }`

#### Scenario: User cancels authentication
- **GIVEN** the biometric prompt is displayed
- **WHEN** the user cancels (clicks cancel or dismisses)
- **THEN** it SHALL return `{ Success: false, ErrorCode: "user_cancelled" }`

#### Scenario: Authentication fails (biometric mismatch)
- **GIVEN** the biometric prompt is displayed
- **WHEN** the biometric scan does not match
- **THEN** it SHALL return `{ Success: false, ErrorCode: "not_recognized" }`

#### Scenario: Biometric not available
- **GIVEN** biometric hardware is not available
- **WHEN** `AuthenticateAsync(reason)` is called
- **THEN** it SHALL return `{ Success: false, ErrorCode: "not_available" }`

### Requirement: Plugin follows IBridgePlugin convention

The biometric plugin SHALL implement `IBridgePlugin` and register via `bridge.UsePlugin<BiometricPlugin>`.

#### Scenario: Plugin registration
- **GIVEN** `bridge.UsePlugin<BiometricPlugin>(serviceProvider)` is called
- **WHEN** the bridge initializes
- **THEN** `IBiometricService` SHALL be exposed to JavaScript as `BiometricService`
- **AND** the JS client SHALL have typed access to `checkAvailability()` and `authenticate(reason)`

### Requirement: Platform providers are injectable

`IBiometricPlatformProvider` SHALL be injectable to allow mock providers for unit testing.

#### Scenario: Mock provider for testing
- **GIVEN** `InMemoryBiometricProvider` is registered as `IBiometricPlatformProvider`
- **WHEN** `AuthenticateAsync(reason)` is called
- **THEN** the mock SHALL return the configured result without platform UI
- **AND** unit tests SHALL be able to verify all code paths

### Requirement: npm package provides TypeScript types

The `@agibuild/bridge-plugin-biometric` npm package SHALL export TypeScript types for the JS client.

#### Scenario: TypeScript types available
- **GIVEN** `@agibuild/bridge-plugin-biometric` is installed
- **WHEN** a developer imports `BiometricService` types
- **THEN** `checkAvailability()` and `authenticate(reason)` SHALL have full IntelliSense
- **AND** `BiometricAvailability` and `BiometricResult` types SHALL be exported
