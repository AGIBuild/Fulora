## 0. Sequencing gate

- [x] 0.1 Mark runtime decomposition as stage-1 prerequisite for `configuration-interface-error-modernization`

## 1. Shared helpers extraction

- [ ] 1.1 Create `RpcMethodHelpers.cs` with unified `SplitRpcMethod` implementation
- [ ] 1.2 Create `UiThreadHelper.cs` with `SafeDispatchToUiThread` method
- [ ] 1.3 Replace all duplicated RPC method parsing in WebViewRpcService and RuntimeBridgeService with RpcMethodHelpers
- [ ] 1.4 Replace all inline UI dispatch patterns in WebViewCore with UiThreadHelper

## 2. RuntimeBridgeService inner type extraction

- [ ] 2.1 Extract `TracingRpcWrapper` to `TracingRpcWrapper.cs`
- [ ] 2.2 Extract `MiddlewareRpcWrapper` to `MiddlewareRpcWrapper.cs`
- [ ] 2.3 Extract `RateLimitMiddleware` to `RateLimitMiddleware.cs`
- [ ] 2.4 Extract `BridgeImportProxy` to `BridgeImportProxy.cs`
- [ ] 2.5 Verify RuntimeBridgeService compiles and all tests pass

## 3. WebViewCore decomposition

- [ ] 3.1 Extract `WebViewNavigationCoordinator` with Navigate, GoBack, GoForward, Refresh, Stop, and navigation events
- [ ] 3.2 Extract `WebViewBridgeCoordinator` with bridge/RPC setup and script injection
- [ ] 3.3 Extract `WebViewOperationQueue` with pending operation management
- [ ] 3.4 Refactor WebViewCore to delegate to coordinators
- [ ] 3.5 Refactor adapter capability discovery to structured pattern

## 4. WebViewShellExperience decomposition

- [ ] 4.1 Extract `NewWindowHandler` from WebViewShellExperience
- [ ] 4.2 Extract `DownloadHandler` from WebViewShellExperience
- [ ] 4.3 Extract `PermissionHandler` from WebViewShellExperience
- [ ] 4.4 Refactor WebViewShellExperience to delegate to handlers

## 5. Verification

- [ ] 5.1 Run full unit test suite — all tests must pass without assertion changes
- [ ] 5.2 Run coverage check — must meet 96% line / 93% branch thresholds
- [ ] 5.3 Run integration tests — behavior must be identical
- [ ] 5.4 Verify no public API surface changes via compilation of dependent projects
