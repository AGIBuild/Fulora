## Context

Fulora's test infrastructure has two tiers:
1. **Unit/Contract tests**: `MockWebViewAdapter` (no real browser, `InvokeScriptAsync` returns configured values, no JS execution)
2. **Integration tests**: In-app ViewModels (`FeatureE2EViewModel`, `ConsumerWebViewE2EViewModel`) that run scenarios in a real desktop app with real WebView

The gap: no way for external test code to drive a Fulora hybrid app, assert on both native and web state, and run in CI. Developers building production apps need xUnit/NUnit tests that launch the app, interact with the WebView, assert bridge calls, and tear down — all headlessly.

**Existing patterns:**
- `Avalonia.Headless.XUnit` for headless Avalonia UI tests
- `TestAppBuilder` in integration tests
- `BridgeEventCollector` / `IBridgeTracer` for observing bridge calls
- `WebView.InvokeScriptAsync()` for executing JS in the WebView

## Goals / Non-Goals

**Goals:**
- `Agibuild.Fulora.Testing.E2E` NuGet package
- `FuloraTestApp` — launches an Avalonia app with WebView in headless mode
- `WebViewTestHandle` — typed API for WebView interaction from test code
- `BridgeTestTracer : IBridgeTracer` — captures bridge calls for assertion
- xUnit integration via `IAsyncLifetime`
- CI-friendly: runs headlessly on Windows/macOS/Linux (where platform WebView is available)

**Non-Goals:**
- Playwright/Selenium feature parity (no full DOM query API)
- Visual regression testing
- Mobile E2E
- Cross-process test orchestration

## Decisions

### D1: Avalonia Headless + Real WebView Adapter

**Choice**: Use `Avalonia.Headless` for the app lifecycle but still attach a real platform WebView adapter for JS execution. On platforms without a display server (Linux CI), fall back to `MockWebViewAdapter` with `ScriptCallback` for basic smoke testing.

**Rationale**: Headless provides app lifecycle without a window. Real WebView adapter enables actual JS execution and bridge testing. Fallback ensures CI works everywhere.

### D2: WebViewTestHandle API

**Choice**:
```csharp
public class WebViewTestHandle
{
    Task<string> EvaluateJsAsync(string script);
    Task WaitForBridgeReadyAsync(TimeSpan? timeout = null);
    Task WaitForElementAsync(string cssSelector, TimeSpan? timeout = null);
    Task ClickElementAsync(string cssSelector);
    Task TypeTextAsync(string cssSelector, string text);
    IReadOnlyList<BridgeCallRecord> GetBridgeCalls(string? serviceFilter = null);
    Task WaitForBridgeCallAsync(string serviceName, string methodName, TimeSpan? timeout = null);
}
```

**Rationale**: Covers essential hybrid E2E needs: JS execution, DOM readiness, bridge call assertion. `WaitForBridgeCallAsync` is the key differentiator — no other tool can assert on typed bridge calls.

### D3: BridgeTestTracer for call capture

**Choice**: `BridgeTestTracer : IBridgeTracer` that records all bridge calls into a `ConcurrentBag<BridgeCallRecord>`. Composed via `CompositeBridgeTracer` with any existing tracers.

`BridgeCallRecord`: `{ ServiceName, MethodName, Direction (Export/Import), ParamsJson, ResultJson?, ErrorMessage?, LatencyMs, Timestamp }`

**Rationale**: Reuses existing `IBridgeTracer` infrastructure. Non-invasive. Can filter/assert on specific calls.

### D4: FuloraTestApp lifecycle

**Choice**:
```csharp
public class FuloraTestApp : IAsyncDisposable
{
    static Task<FuloraTestApp> LaunchAsync(Action<AppBuilder> configure);
    WebViewTestHandle GetWebView(string? name = null);
    T GetService<T>();
    Task ShutdownAsync();
}
```

Usage:
```csharp
[Fact]
public async Task UserCanLoginViaBridge()
{
    await using var app = await FuloraTestApp.LaunchAsync(builder => {
        builder.UseFulora(f => f.AddBridge(b => b.Expose<IAuthService>(new AuthService())));
    });
    
    var wv = app.GetWebView();
    await wv.WaitForBridgeReadyAsync();
    await wv.TypeTextAsync("#email", "user@test.com");
    await wv.ClickElementAsync("#login-btn");
    
    await wv.WaitForBridgeCallAsync("AuthService", "Login");
    var calls = wv.GetBridgeCalls("AuthService");
    Assert.Single(calls);
}
```

**Rationale**: Familiar xUnit pattern. `IAsyncDisposable` for cleanup. `LaunchAsync` encapsulates headless app setup.

### D5: DOM interaction via InvokeScriptAsync

**Choice**: `ClickElementAsync`, `TypeTextAsync`, `WaitForElementAsync` are implemented as JS script injection via `WebView.InvokeScriptAsync`. Example: `WaitForElementAsync("#btn")` → polls `document.querySelector('#btn')` until non-null.

**Rationale**: No external browser automation dependency. Works with any WebView adapter that supports `InvokeScriptAsync`. Simple, self-contained.

## Risks / Trade-offs

- **[Risk] Platform WebView in CI** → Linux CI may lack a display server for WebKitGTK. Use `xvfb` or fall back to mock.
- **[Risk] Timing/flakiness** → Poll-based waits can be flaky. Use configurable timeouts with exponential backoff.
- **[Trade-off] Limited DOM API** → No full DOM traversal. Accept as scope limit — this is bridge-focused E2E, not browser testing.

## Testing Strategy

- **CT**: `BridgeTestTracer` records calls correctly
- **CT**: `WebViewTestHandle` JS generation (correct selectors, polling logic)
- **IT**: E2E test of the showcase-todo app using `FuloraTestApp`
