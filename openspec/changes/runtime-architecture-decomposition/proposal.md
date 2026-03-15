## Why

Three core Runtime files exceed maintainable size: WebViewCore.cs (1,430 lines), WebViewShellExperience.cs (1,248 lines), and RuntimeBridgeService.cs (799 lines). Each mixes multiple responsibilities: WebViewCore handles navigation, bridge, operations queue, adapter event dispatch, and feature coordination in a single class. Code patterns are duplicated across these files (RPC method parsing, UI thread dispatch, adapter capability discovery). This makes reasoning about changes risky and slows down development velocity. Decomposing into focused components aligns with the project's internal-restructuring spec and advances long-term maintainability — a post-Phase 12 priority.

## Non-goals

- Changing any public API surface (IWebView, IBridgeService, etc.)
- Changing observable behavior or event ordering semantics
- Adding new features — this is purely structural
- Modifying the source generator or TypeScript generation pipeline

## What Changes

- **WebViewCore decomposition**: Extract `WebViewNavigationCoordinator`, `WebViewBridgeCoordinator`, `WebViewOperationQueue`, and `WebViewFeatureCoordinator` from WebViewCore. WebViewCore becomes a facade delegating to these components.
- **WebViewShellExperience decomposition**: Extract `NewWindowHandler`, `DownloadHandler`, `PermissionHandler` as separate handler classes. WebViewShellExperience becomes a coordinator.
- **RuntimeBridgeService cleanup**: Move `TracingRpcWrapper`, `MiddlewareRpcWrapper`, `RateLimitMiddleware`, and `BridgeImportProxy` inner types to their own files.
- **Shared helpers extraction**:
  - `RpcMethodHelpers` — unified `SplitRpcMethod` / `ExtractMethodName`
  - `UiThreadDispatcher` helper — `SafeDispatchToUiThread` replacing 7+ identical dispatch blocks
  - Adapter capability registry pattern replacing `is` check chains in WebViewCore constructor

## Execution Order

- This change is the first modernization stage and MUST land before `configuration-interface-error-modernization`.
- Rationale: interface/options/error-model updates depend on stable, decomposed Runtime boundaries to avoid cross-change conflicts.

## Capabilities

### New Capabilities
- `runtime-structural-decomposition`: Rules and boundaries for decomposing Runtime classes into focused components

### Modified Capabilities
- `internal-restructuring`: Extended with specific decomposition constraints for WebViewCore, WebViewShellExperience, and RuntimeBridgeService

## Impact

- **Runtime files**: `WebViewCore.cs`, `WebViewShellExperience.cs`, `RuntimeBridgeService.cs`, `WebViewRpcService.cs` — all refactored
- **New files**: ~10 new files in `src/Agibuild.Fulora.Runtime/` (coordinators, handlers, helpers)
- **Tests**: No test assertion changes expected — behavior is preserved. Test coverage must remain >= 96% line, >= 93% branch.
- **No public API changes**: All types remain internal to the Runtime assembly
