## ADDED Requirements

### Requirement: WebViewCore facade pattern
WebViewCore SHALL delegate to focused coordinator components. It MUST retain its role as the public facade implementing IWebView but SHALL NOT contain business logic exceeding lifecycle orchestration and delegation.

#### Scenario: Navigation delegated to coordinator
- **WHEN** WebViewCore.Navigate() is called
- **THEN** WebViewCore MUST delegate to WebViewNavigationCoordinator without containing navigation logic itself

#### Scenario: Bridge setup delegated to coordinator
- **WHEN** bridge services are registered via WebViewCore
- **THEN** WebViewCore MUST delegate to WebViewBridgeCoordinator

### Requirement: Shared UI thread dispatch helper
The Runtime SHALL provide a `SafeDispatchToUiThread` helper that encapsulates the disposed/destroyed check and dispatcher access check pattern.

#### Scenario: Event handler dispatch
- **WHEN** an adapter event handler needs to dispatch to the UI thread
- **THEN** it MUST use SafeDispatchToUiThread instead of inline disposed/dispatcher checks

#### Scenario: Disposed state check
- **WHEN** SafeDispatchToUiThread is called after disposal
- **THEN** it MUST return without executing the action

### Requirement: Shared RPC method helpers
The Runtime SHALL provide `RpcMethodHelpers` with a single `SplitRpcMethod` implementation replacing the duplicated implementations in WebViewRpcService and RuntimeBridgeService.

#### Scenario: Consistent method name parsing
- **WHEN** SplitRpcMethod is called with "ServiceName.methodName"
- **THEN** it MUST return ("ServiceName", "methodName") identically to the existing behavior

### Requirement: Bridge service inner types extracted
RuntimeBridgeService inner types (TracingRpcWrapper, MiddlewareRpcWrapper, RateLimitMiddleware, BridgeImportProxy) SHALL each reside in their own source file within the Runtime project.

#### Scenario: File organization
- **WHEN** a developer needs to modify RateLimitMiddleware
- **THEN** they MUST find it in its own file, not nested inside RuntimeBridgeService

### Requirement: Shell experience handler decomposition
WebViewShellExperience SHALL delegate domain-specific handling to separate handler classes for new-window, download, and permission concerns.

#### Scenario: New window request handling
- **WHEN** a new window request is received
- **THEN** WebViewShellExperience MUST delegate to a dedicated NewWindowHandler

### Requirement: Adapter capability discovery pattern
WebViewCore SHALL use a structured capability discovery mechanism instead of sequential `is` type-check chains in the constructor.

#### Scenario: Adapter capability registration
- **WHEN** WebViewCore is constructed with an adapter
- **THEN** adapter capabilities MUST be discovered and registered via a structured mechanism, not inline is-checks
