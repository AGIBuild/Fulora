## Context

WebViewCore.cs (1,430 lines) mixes navigation, bridge, operations queue, feature coordination, and adapter event dispatch. WebViewShellExperience.cs (1,248 lines) combines new-window, download, permission, session, and policy handling. RuntimeBridgeService.cs (799 lines) bundles orchestration with inner wrapper/middleware/proxy types. Code patterns are duplicated: RPC method parsing in 2 places, UI dispatch pattern in 7+ places, adapter capability discovery via 10+ `is` checks.

## Goals / Non-Goals

**Goals:**
- Reduce per-file complexity by extracting focused coordinator/handler components
- Eliminate code duplication through shared helpers
- Maintain all public API surfaces and observable behavior
- Keep all extracted types internal to Agibuild.Fulora.Runtime

**Non-Goals:**
- Changing any public type signature or namespace
- Changing event ordering or threading semantics
- Adding new features or capabilities

## Decisions

1. **Coordinator extraction strategy**: WebViewCore retains the IWebView implementation but delegates to internal coordinators. Coordinators receive the adapter and dispatcher via constructor injection from WebViewCore. This preserves the single public entry point.

2. **WebViewNavigationCoordinator**: Owns Navigate, GoBack, GoForward, Refresh, Stop, and navigation event handling. ~200 lines extracted.

3. **WebViewBridgeCoordinator**: Owns bridge setup, RPC service lifecycle, script injection. ~150 lines extracted.

4. **WebViewOperationQueue**: Owns the pending operation queue and deferred execution logic. ~100 lines extracted.

5. **SafeDispatchToUiThread**: A static helper method on a `UiThreadHelper` class: `static void SafeDispatch(IWebViewDispatcher dispatcher, ref bool disposed, ref bool adapterDestroyed, Action action)`. Replaces 7+ identical inline blocks.

6. **RpcMethodHelpers**: Static class with `(string Service, string Method) SplitRpcMethod(string rpcMethod)`. Single implementation used by both WebViewRpcService and RuntimeBridgeService.

7. **Shell handler extraction**: NewWindowHandler, DownloadHandler, PermissionHandler as internal classes instantiated by WebViewShellExperience. Each handler receives the policy and adapter references.

8. **Inner type file extraction**: TracingRpcWrapper.cs, MiddlewareRpcWrapper.cs, RateLimitMiddleware.cs, BridgeImportProxy.cs as separate files. No behavioral changes.

## Risks / Trade-offs

- **InternalsVisibleTo scope**: Test projects already have InternalsVisibleTo for Runtime. Extracted internal types remain testable.
- **Constructor complexity**: WebViewCore constructor currently does capability discovery and coordinator setup. This becomes cleaner with structured discovery but the constructor parameter list grows. Mitigate by using a setup method.
- **Merge risk**: WebViewCore is actively used. Coordinate with any in-flight changes.
