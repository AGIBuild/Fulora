## Purpose

Define the core IWebViewRpcService interface contract for bidirectional JS-C# RPC communication, including handler registration, invocation, and JS stub idempotency.

## Requirements

### Requirement: IWebViewRpcService interface in Core
The Core assembly SHALL define `IWebViewRpcService`:
- `void Handle(string method, Func<JsonElement?, Task<object?>> handler)` — register async C# handler
- `void Handle(string method, Func<JsonElement?, object?> handler)` — register sync C# handler
- `void RemoveHandler(string method)` — unregister handler
- `Task<JsonElement> InvokeAsync(string method, object? args = null)` — call JS handler, raw result
- `Task<T?> InvokeAsync<T>(string method, object? args = null)` — call JS handler, typed result

The `JsStub` static property SHALL remain the canonical source for the base RPC JS stub. This stub SHALL be safe to inject multiple times (idempotent).

#### Scenario: IWebViewRpcService is resolvable
- **WHEN** a consumer references `IWebViewRpcService`
- **THEN** it compiles without missing type errors

#### Scenario: JsStub is idempotent
- **WHEN** `WebViewRpcService.JsStub` is injected into a page that already has it
- **THEN** the bridge SHALL continue to function correctly without errors
