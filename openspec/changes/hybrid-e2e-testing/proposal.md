## Why

Fulora has excellent unit testing (MockBridge, MockWebViewAdapter) but no solution for end-to-end testing of hybrid apps where test code drives both native UI and WebView content in a single test. Developers building production apps need CI-friendly E2E tests that assert on bridge calls, DOM state, and native UI together. Neither Playwright (Electron-only) nor Appium (no WebView bridge awareness) solve this. Goal: G4 (Contract-Driven Testability), Phase 12 — Production Testing Confidence.

## What Changes

- New test library `Agibuild.Fulora.Testing.E2E` providing a hybrid E2E test harness
- `FuloraTestApp` — programmatic app launcher with headless Avalonia + real WebView adapter
- `WebViewTestHandle` — typed API for interacting with WebView content:
  - `InvokeScriptAsync()` for arbitrary JS execution
  - `WaitForBridgeReady()` for bridge initialization
  - `WaitForElement(selector)` for DOM readiness
  - `GetBridgeCalls()` for bridge call assertion (via test-mode tracer)
- `BridgeTestTracer` — captures all bridge calls for assertion in tests
- Integration with xUnit via `IAsyncLifetime` for setup/teardown
- Headless mode support for CI (Avalonia Headless + platform WebView where available)

## Capabilities

### New Capabilities

- `hybrid-e2e-testing`: E2E test harness for Fulora hybrid applications

### Modified Capabilities

- `webview-testing-harness`: Add E2E layer on top of existing mock-based testing

## Non-goals

- Full Playwright/Selenium feature parity
- Visual regression testing (screenshot comparison)
- Mobile E2E (desktop platforms first)
- Testing non-Fulora apps

## Impact

- New: `tests/Agibuild.Fulora.Testing.E2E/` project
- New: `FuloraTestApp`, `WebViewTestHandle`, `BridgeTestTracer` classes
- New: NuGet package `Agibuild.Fulora.Testing.E2E` for consumer test projects
- New: documentation for hybrid E2E testing patterns
- New: example tests in integration test project
