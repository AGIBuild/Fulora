# Hybrid E2E Testing — Tasks

## 1. Test Library Project

- [x] 1.1 Add to existing `tests/Agibuild.Fulora.Testing/` project
- [x] 1.2 Dependencies: existing Fulora.Core, Fulora.Runtime references

## 2. BridgeTestTracer

- [x] 2.1 Create `BridgeTestTracer : IBridgeTracer`
- [x] 2.2 Record all bridge calls into `ConcurrentBag<BridgeCallRecord>`
- [x] 2.3 Define `BridgeCallRecord`: ServiceName, MethodName, Direction, ParamsJson, ResultType, ErrorMessage, ElapsedMs, Timestamp
- [x] 2.4 Implement `GetBridgeCalls(string? serviceFilter)` query method
- [x] 2.5 Implement `WaitForBridgeCallAsync(service, method, timeout)` with async wait
- [x] 2.6 Implement `Reset()` to clear recorded calls
- [x] 2.7 CT: tracer records export and import calls correctly
- [x] 2.8 CT: filter by service name
- [x] 2.9 CT: WaitForBridgeCallAsync completes when matching call arrives
- [x] 2.10 CT: WaitForBridgeCallAsync throws TimeoutException when no call arrives

## 3. WebViewTestHandle

- [x] 3.1 Create `WebViewTestHandle` wrapping a `WebViewCore` instance
- [x] 3.2 Implement `EvaluateJsAsync(script)` → `WebViewCore.InvokeScriptAsync`
- [x] 3.3 Implement `WaitForBridgeReadyAsync(timeout)` — polls `window.__agibuild?.bridge?.ready`
- [x] 3.4 Implement `WaitForElementAsync(selector, timeout)` — polls `document.querySelector(selector)`
- [x] 3.5 Implement `ClickElementAsync(selector)` — `document.querySelector(selector).click()`
- [x] 3.6 Implement `TypeTextAsync(selector, text)` — set value + dispatch input/change events
- [x] 3.7 Wire `BridgeTestTracer` for `GetBridgeCalls` and `WaitForBridgeCallAsync` delegation
- [x] 3.8 CT: WebViewTestHandle.EvaluateJsAsync delegates to core

## 4. FuloraTestApp

- [x] 4.1 Create `FuloraTestApp` with `Create()` static factory
- [x] 4.2 Initialize with MockWebViewAdapter + TestDispatcher + BridgeTestTracer
- [x] 4.3 Implement `GetWebView()` → `WebViewTestHandle`
- [x] 4.4 Implement `DisposeAsync()` / `IAsyncDisposable`
- [x] 4.5 CT: Create returns configured app
- [x] 4.6 CT: DisposeAsync cleans up

## 5. Example Tests

- [ ] 5.1 Create example E2E test: navigate to a page and verify title via `EvaluateJsAsync`
- [ ] 5.2 Create example E2E test: bridge call assertion
- [ ] 5.3 Create example E2E test: showcase-todo app

## 6. CI Integration

- [ ] 6.1 Document CI setup: macOS/Windows with real adapter, Linux with mock fallback
- [ ] 6.2 Add E2E test lane to Nuke build (optional, platform-gated)
- [ ] 6.3 Document limitations: mock adapter doesn't execute JS
