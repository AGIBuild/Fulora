## Why

Modern desktop and mobile applications increasingly require biometric authentication (Face ID, Touch ID, Windows Hello, Android BiometricPrompt). Fulora's Auth Token plugin already uses platform secure storage (Keychain, Credential Manager, Keystore), but there is no way to gate access with biometric verification. This is a natural extension that signals "modern, production-ready framework" to evaluators. Goal: G3 (Secure by Default), Phase 12 — Enterprise Security Patterns.

## What Changes

- New plugin `Agibuild.Fulora.Plugin.Biometric` with `IBiometricService` (`[JsExport]`)
- Platform provider abstraction `IBiometricPlatformProvider` with implementations:
  - macOS/iOS: `LocalAuthentication` framework (`LAContext`)
  - Windows: `Windows.Security.Credentials.UI.UserConsentVerifier` (Windows Hello)
  - Android: `BiometricPrompt` API
  - Linux: stub returning `NotSupported`
- npm package `@agibuild/bridge-plugin-biometric` with TypeScript types
- Integration example showing biometric-gated token access (AuthToken + Biometric)

## Capabilities

### New Capabilities

- `biometric-auth-plugin`: Cross-platform biometric authentication via bridge

### Modified Capabilities

- (None — new standalone plugin)

## Non-goals

- FIDO2/WebAuthn protocol (different concern — web standard auth)
- Continuous biometric monitoring
- Biometric enrollment management (use OS settings)
- Replacing platform Keychain/CredMgr auth prompts

## Impact

- New: `plugins/Agibuild.Fulora.Plugin.Biometric/` (plugin + service + providers)
- New: `packages/bridge-plugin-biometric/` (npm package)
- New: CT for `BiometricService` with mock provider
- New: documentation in plugin authoring guide
