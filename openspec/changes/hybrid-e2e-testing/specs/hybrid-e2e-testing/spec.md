## Purpose

Define requirements for the hybrid E2E testing library. Ensures developers can write xUnit tests that launch a Fulora app, interact with WebView content, assert on bridge calls, and run in CI.

## ADDED Requirements

### Requirement: FuloraTestApp launches a headless Avalonia app with WebView

`FuloraTestApp` SHALL launch a headless Avalonia app with WebView for E2E testing.

#### Scenario: Launch test app
- **GIVEN** a test class using `FuloraTestApp`
- **WHEN** `FuloraTestApp.LaunchAsync(configure)` is called
- **THEN** an Avalonia app SHALL start in headless mode
- **AND** the configured WebView SHALL be created with bridge services
- **AND** the app SHALL be ready for interaction

#### Scenario: Shutdown cleans up resources
- **GIVEN** a running `FuloraTestApp`
- **WHEN** `ShutdownAsync()` is called (or `DisposeAsync` via `await using`)
- **THEN** all WebViews SHALL be detached
- **AND** the Avalonia app SHALL shut down cleanly
- **AND** no background tasks SHALL remain

### Requirement: WebViewTestHandle provides typed WebView interaction

`WebViewTestHandle` SHALL provide typed methods for JavaScript execution, DOM waiting, and user interaction simulation.

#### Scenario: Execute JavaScript
- **GIVEN** a `WebViewTestHandle` for a loaded WebView
- **WHEN** `EvaluateJsAsync("document.title")` is called
- **THEN** it SHALL return the page title as a string

#### Scenario: Wait for bridge ready
- **GIVEN** a WebView navigating to a page with `@agibuild/bridge`
- **WHEN** `WaitForBridgeReadyAsync()` is called
- **THEN** it SHALL wait until the bridge is initialized on the JS side
- **AND** SHALL return within the configured timeout
- **AND** SHALL throw `TimeoutException` if bridge is not ready in time

#### Scenario: Wait for DOM element
- **GIVEN** a WebView with dynamically rendered content
- **WHEN** `WaitForElementAsync("#login-form")` is called
- **THEN** it SHALL poll `document.querySelector("#login-form")` until it exists
- **AND** SHALL return when the element is found
- **AND** SHALL throw `TimeoutException` if not found within timeout

#### Scenario: Click element
- **GIVEN** a WebView with a `<button id="submit">` element
- **WHEN** `ClickElementAsync("#submit")` is called
- **THEN** the button's click event SHALL fire in the web content

#### Scenario: Type text into input
- **GIVEN** a WebView with an `<input id="email">` element
- **WHEN** `TypeTextAsync("#email", "user@test.com")` is called
- **THEN** the input's value SHALL be set to "user@test.com"
- **AND** `input` and `change` events SHALL fire

### Requirement: BridgeTestTracer captures bridge calls for assertion

`BridgeTestTracer` SHALL capture bridge calls for assertion and support waiting for specific calls.

#### Scenario: Capture export call
- **GIVEN** a `BridgeTestTracer` composed with the bridge tracer
- **WHEN** JS calls `appService.getData()` via bridge
- **THEN** `GetBridgeCalls()` SHALL contain a record with `ServiceName: "AppService"`, `MethodName: "GetData"`, `Direction: Export`
- **AND** the record SHALL include `ParamsJson`, `ResultJson`, `LatencyMs`

#### Scenario: Wait for specific bridge call
- **GIVEN** a `BridgeTestTracer` is active
- **WHEN** `WaitForBridgeCallAsync("AuthService", "Login")` is called
- **THEN** it SHALL block until a bridge call to `AuthService.Login` is recorded
- **AND** SHALL return the `BridgeCallRecord`
- **AND** SHALL throw `TimeoutException` if the call does not occur

#### Scenario: Filter bridge calls by service
- **GIVEN** a `BridgeTestTracer` that has recorded calls to multiple services
- **WHEN** `GetBridgeCalls("AppService")` is called
- **THEN** it SHALL return only calls to `AppService`

### Requirement: CI-friendly headless execution

E2E tests SHALL run in CI environments without a display server (Linux with xvfb, macOS with real adapter).

#### Scenario: Tests run on CI without display
- **GIVEN** a Linux CI environment without a display server
- **WHEN** E2E tests are executed with `xvfb-run` or mock adapter fallback
- **THEN** tests SHALL pass (with mock adapter limitations documented)
- **AND** no graphical window SHALL be required

#### Scenario: Tests run on macOS CI
- **GIVEN** a macOS CI environment (GitHub Actions)
- **WHEN** E2E tests are executed
- **THEN** tests SHALL use the real WKWebView adapter
- **AND** bridge calls SHALL work end-to-end
