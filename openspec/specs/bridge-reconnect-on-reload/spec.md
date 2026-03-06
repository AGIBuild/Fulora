## Purpose

Define requirements for automatic bridge reconnection after page navigation or reload, ensuring the JS-C# bridge remains functional without requiring an application restart.

## Requirements

### Requirement: Bridge JS stubs are re-injected after successful navigation
When the WebMessage bridge is enabled and a navigation completes successfully (including page reload), the system SHALL re-inject the base RPC JS stub (`WebViewRpcService.JsStub`) and all currently-exposed service JS stubs so that the frontend can reconnect to the bridge without requiring an application restart.

#### Scenario: Page reload re-injects bridge stubs
- **WHEN** the bridge is enabled and services have been exposed
- **AND** the user triggers a page reload (right-click → Reload or programmatic)
- **AND** the navigation completes successfully
- **THEN** the base RPC JS stub SHALL be re-injected
- **AND** all exposed service JS stubs SHALL be re-injected
- **AND** the frontend SHALL be able to communicate with C# services again

#### Scenario: Navigation to a new URL re-injects bridge stubs
- **WHEN** the bridge is enabled and services have been exposed
- **AND** a navigation to a new URL completes successfully
- **THEN** the base RPC JS stub and all service stubs SHALL be re-injected

#### Scenario: Failed navigation does not re-inject stubs
- **WHEN** the bridge is enabled
- **AND** a navigation fails (network error, canceled, etc.)
- **THEN** the system SHALL NOT attempt to re-inject bridge stubs

#### Scenario: Stubs not re-injected when bridge is disabled
- **WHEN** the bridge is NOT enabled (`_webMessageBridgeEnabled` is false)
- **AND** a navigation completes successfully
- **THEN** the system SHALL NOT inject any bridge stubs

### Requirement: Service JS stubs are cached for reinsertion
The `RuntimeBridgeService` SHALL cache the JS stub string for each exposed service so that stubs can be re-injected on navigation without regeneration.

#### Scenario: Source-generated service stub is cached
- **WHEN** a service is exposed via source-generated registration
- **THEN** the JS stub returned by `generated.GetJsStub()` SHALL be stored in the service's metadata

#### Scenario: Reflection-based service stub is cached
- **WHEN** a service is exposed via reflection-based registration
- **THEN** the JS stub returned by `GenerateJsStub()` SHALL be stored in the service's metadata

### Requirement: Reinject ordering — base stub before service stubs
The base RPC JS stub (`WebViewRpcService.JsStub`) SHALL be injected before any service-specific stubs during re-injection, ensuring `window.agWebView` and `window.agWebView.rpc` are available when service stubs execute.

#### Scenario: Base RPC stub injected before service stubs
- **WHEN** re-injection occurs after navigation
- **THEN** the `WebViewRpcService.JsStub` script SHALL be invoked before any service JS stubs

### Requirement: Stub re-injection is idempotent
Re-injecting stubs on a page that already has them (e.g., during initial load where both `EnableWebMessageBridge`/`Expose` and navigation completion fire) SHALL NOT cause errors or duplicate registrations.

#### Scenario: Double injection on initial load is harmless
- **WHEN** `EnableWebMessageBridge` injects the base stub
- **AND** the initial navigation completes and re-injection fires
- **THEN** the bridge SHALL function correctly without errors or duplicate behavior
