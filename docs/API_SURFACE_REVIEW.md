# API Surface Review — Agibuild.Fulora

**Date**: 2026-03-02
**Status**: 1.0 Freeze Audit
**Test Count**: 1668 CTs passing (1459 unit + 209 integration)
**Inventory**: `docs/API_SURFACE_INVENTORY.release.txt` (Core: 72 types, Runtime: 100 types)

---

## Summary

The public API surface is frozen for the 1.0 GA release. All previously identified action items from the pre-1.0 audit (2026-02-17) have been resolved. Phase 8 additions (deep-link registration, SPA hot update, Bridge V2 capabilities) have been audited for naming consistency and interface coverage.

---

## 1.0 Freeze Status

### IWebView Interface Coverage (Resolved ✅)

The following members have been promoted to `IWebView` (additive, non-breaking):

| Member | Status |
|--------|--------|
| `GetZoomFactorAsync` / `SetZoomFactorAsync` | ✅ On IWebView |
| `FindInPageAsync` / `StopFindInPageAsync` | ✅ On IWebView |
| `AddPreloadScriptAsync` / `RemovePreloadScriptAsync` | ✅ On IWebView |
| `ContextMenuRequested` | ✅ On IWebView |

The following remain on concrete types only (by design):

| Member | Rationale |
|--------|-----------|
| `SetCustomUserAgent` | Init-time configuration; not a runtime feature |
| `EnableWebMessageBridge` / `DisableWebMessageBridge` | Advanced plumbing; auto-managed by Bridge property |
| `EnableSpaHosting` | Configuration via `WebViewEnvironmentOptions`; deferred to post-1.0 |

### Experimental Attributes (Resolved ✅)

| Code | Type | 1.0 Decision | Rationale |
|------|------|-------------|-----------|
| AGWV001 | `ICookieManager` | **Keep experimental (pending graduation)** | All five platform adapters (Windows/macOS/iOS/Android/Gtk) now implement `ICookieAdapter` end-to-end; see `tests/Agibuild.Fulora.UnitTests/AdapterCookieContract.cs` for the shared cross-platform behavior contract. Graduation is scheduled for v2.0 (see `docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md` Task 4) once the same contract runs in every platform-specific integration lane. The earlier audit entry citing "Android adapter returns null" is superseded by the 1.6.x implementation. |
| AGWV004 | `WebResourceRequestedEventArgs` | **Graduated** | Attribute removed. Stable and widely used across all adapters. |
| AGWV005 | `EnvironmentRequestedEventArgs` | **Keep experimental (pending decision)** | Placeholder with no concrete implementation. v2.0 plan proposes either implement-and-graduate or remove; see `docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md` Task 5. |

### Phase 8 Additions Audit (New ✅)

Phase 8 introduced the following public API surface, all passing naming convention validation:

| Category | Key Public Types | Convention Check |
|----------|-----------------|-----------------|
| Deep Link Registration | `DeepLinkActivationEnvelope`, `DeepLinkRegistrationService`, `IDeepLinkRegistrationService`, `IDeepLinkAdmissionPolicy`, `DeepLinkRouteDeclaration`, `DeepLinkPlatformEntrypoint` | ✅ PascalCase, I* prefix, *EventArgs suffix |
| Deep Link Diagnostics | `DeepLinkDiagnosticEventArgs`, `DeepLinkDiagnosticEventType` | ✅ Follows .NET event pattern |
| Deep Link Results | `DeepLinkRegistrationResult`, `DeepLinkActivationIngressResult`, `DeepLinkAdmissionDecision` | ✅ Record pattern with factory methods |
| SPA Hot Update | `SpaAssetHotUpdateService`, `SpaAssetHotUpdateResult` | ✅ PascalCase |
| Bridge V2 | `JsExportAttribute`, `JsImportAttribute`, `IBridgeService`, `BridgeCallOptions` | ✅ PascalCase, I* prefix, *Attribute suffix |

### Type Naming Consistency ✅

All 172 public types (72 Core + 100 Runtime) follow .NET naming conventions:
- `I*` prefix for interfaces
- `*Attribute` suffix for attributes
- `*EventArgs` suffix for event argument types
- PascalCase throughout
- No convention violations detected

---

## Action Items Resolution

| # | Action | Original Priority | Resolution |
|---|--------|----------|-----------|
| 1 | Promote Zoom/Find/Preload/ContextMenu to IWebView | High | ✅ Completed — all promoted to IWebView |
| 2 | Expose EnableSpaHosting on WebView control | Medium | ✅ Completed in 1.6.x — `Avalonia.WebView` implements `ISpaHostingWebView`; the marker-interface downcast itself is scheduled for v2.0 retirement |
| 3 | Graduate AGWV004 from Experimental | Low | ✅ Completed — attribute removed |
| 4 | Document event lifecycle on WebView control | Low | ✅ Documented — pre-attach subscription is no-op by design for Avalonia lifecycle |
| 5 | Version as 1.0.0-preview.1 → 1.0.0 | — | ✅ Released; current release line 1.6.x |

---

## Evidence Pointers (API → Executable)

- **Navigation cancel semantics**: `tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1NavigationTests.cs`
- **Native navigation cancel**: `tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1NativeNavigationTests.cs`
- **WebMessage bridge policy/drop**: `tests/Agibuild.Fulora.UnitTests/WebViewCoreHotspotCoverageTests.cs`
- **DevTools toggle (core)**: `tests/Agibuild.Fulora.UnitTests/RuntimeCoverageTests.cs`
- **DevTools toggle (GTK smoke)**: `tests/Agibuild.Fulora.Integration.Tests/Agibuild.Fulora.Integration.Tests/ViewModels/GtkWebViewSmokeViewModel.cs`
- **Deep link registration**: `tests/Agibuild.Fulora.UnitTests/DeepLinkRegistrationServiceTests.cs`
- **Deep link activation**: `tests/Agibuild.Fulora.UnitTests/DeepLinkActivationTests.cs`
- **Bridge cancellation**: `tests/Agibuild.Fulora.UnitTests/BridgeCancellationTests.cs`
- **Bridge streaming**: `tests/Agibuild.Fulora.UnitTests/BridgeStreamingTests.cs`
- **SPA hot update**: `tests/Agibuild.Fulora.UnitTests/SpaAssetHotUpdateTests.cs`

---

## Public API Inventory (generated)

Snapshot source:
- `dotnet build -c Release` (2026-03-01)
- Generator: `tools/gen-api-inventory.csx`

<details><summary>Inventory (Release)</summary>

```text
See `docs/API_SURFACE_INVENTORY.release.txt`.
```

</details>

---

## 1.6 Capability Split (P5, Additive)

**Date**: 2026-04-15
**Status**: Additive, source-compatible. No existing members removed or renamed.

### New Capability Interfaces (13 total — 12 composed into `IWebViewFeatures`, 1 into `IWebViewScript`)

| Interface | Members | Replaces / Extracted from |
|---|---|---|
| `IWebViewDevTools` | `OpenDevToolsAsync`, `CloseDevToolsAsync`, `IsDevToolsOpenAsync` | `IWebViewFeatures` |
| `IWebViewScreenshot` | `CaptureScreenshotAsync` | `IWebViewFeatures` |
| `IWebViewPrinting` | `PrintToPdfAsync` | `IWebViewFeatures` |
| `IWebViewZoom` | `GetZoomFactorAsync`, `SetZoomFactorAsync` | `IWebViewFeatures` |
| `IWebViewFindInPage` | `FindInPageAsync`, `StopFindInPageAsync` | `IWebViewFeatures` |
| `IWebViewPreloadScripts` | `AddPreloadScriptAsync`, `RemovePreloadScriptAsync` | `IWebViewScript` |
| `IWebViewNativeHandle` | `TryGetWebViewHandleAsync` | `IWebViewFeatures` |
| `IWebViewDownloads` | `DownloadRequested` event | `IWebViewFeatures` |
| `IWebViewPermissions` | `PermissionRequested` event | `IWebViewFeatures` |
| `IWebViewContextMenu` | `ContextMenuRequested` event | `IWebViewFeatures` |
| `IWebViewPopupWindows` | `NewWindowRequested` event | `IWebViewFeatures` |
| `IWebViewResourceInterception` | `WebResourceRequested`, `EnvironmentRequested` events | `IWebViewFeatures` |
| `IWebViewLifecycleEvents` | `AdapterCreated`, `AdapterDestroyed` events | `IWebViewFeatures` |

### Unchanged Surface

- `IWebView` still derives from `IWebViewNavigation + IWebViewScript + IWebViewBridge + IWebViewFeatures` — no member visible to consumers moves or changes.
- `IWebViewFeatures` is kept as an empty interface that inherits the 12 capability interfaces. Every member signature is identical.
- `IWebViewScript` keeps `InvokeScriptAsync` plus, via inheritance from `IWebViewPreloadScripts`, the two preload methods.
- `IWebViewBridge` is **not** modified in this change — its `TryGet*Manager` and `ChannelId` cleanup is tracked separately for v2.0.

### Migration

No action required. Existing code compiles unchanged. New code can now declare dependencies on a single capability (e.g. `ctor(IWebViewDevTools dt)`) instead of `IWebView`, enabling narrower unit-test doubles and cleaner Dependency Injection registrations.

---

## 1.6 Cookie Adapter Contract Evidence (Additive)

**Date**: 2026-04-23
**Status**: Additive test-only artifact. No public API changes.

### New shared-contract fixture

- `tests/Agibuild.Fulora.UnitTests/AdapterCookieContract.cs` — a factory-delegate-driven helper set that defines the behavior every `ICookieAdapter` implementation must satisfy (positive Set/Get/Delete/ClearAll semantics, overwrite rules, idempotency). Null-argument guards are intentionally out of contract scope because the runtime funnels all calls through `RuntimeCookieManager`, which already performs `ArgumentNullException.ThrowIfNull`.
- `tests/Agibuild.Fulora.UnitTests/MockCookieAdapterContractTests.cs` — runs the full contract against the in-memory `MockWebViewAdapterWithCookies`. Seven facts; all pass in 1.6.6.

### Purpose

Establish the reusable cross-platform test harness that the v2.0 plan requires as a precondition for graduating `AGWV001 ICookieManager`. Platform-specific integration lanes (Windows WebView2, macOS/iOS WKWebView, Linux WebKitGtk, Android WebView) are expected to mirror this test class with a real adapter factory; once every lane is green, the `[Experimental("AGWV001")]` attribute is removed in the v2.0 commit tracked by `docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md` Task 4.

### Scope decision: contract vs. adapter extensibility

`ICookieAdapter` remains `internal` in 1.6.x. Making it public is a separate v2.0 decision because it advertises adapter authorship as a supported extension point, which requires versioning commitments we are not ready to make in 1.x. The contract fixture works around the visibility constraint with a factory-delegate pattern (public test class, static helpers taking `AdapterFactory`) rather than an abstract base class.

---

## 1.6 Navigation SSL Policy Explicit (Additive)

**Date**: 2026-04-25
**Status**: Additive, internal-only types. **No public API delta** — verifiable via `docs/API_SURFACE_INVENTORY.release.txt` (unchanged).

### New internal contracts

- `Agibuild.Fulora.Core.Security.INavigationSecurityHooks`
- `Agibuild.Fulora.Core.Security.NavigationSecurityDecision` (enum; `Reject` only in v1)
- `Agibuild.Fulora.Core.Security.ServerCertificateErrorContext` (record)
- `Agibuild.Fulora.Core.Security.DefaultNavigationSecurityHooks` (singleton)
- Extended constructor on `WebViewSslException(ServerCertificateErrorContext, Guid)` — additive only.

### Purpose

Every platform WebView adapter routes server-certificate failures through one strategy interface; the only v1 decision is `Reject`. v2.0 (see `docs/superpowers/plans/2026-04-25-fulora-v2-apple-shim-modernization.md` Phase 4 plus the v2 public-API plan) promotes the interface to public and adds per-domain pinning.

### Scope decision

All new security-hook types remain `internal` in 1.6.x; hosts observe behavior through existing `NavigationCompleted(Failure, WebViewSslException)` only. Promotion and pinning policy are explicitly deferred to v2.0.

### Per-platform certificate metadata capability

| Platform | Host | Summary | Subject | Issuer | Validity |
|----------|------|---------|---------|--------|----------|
| Windows (WebView2) | ✅ | ✅ | ✅ | ✅ | ✅ |
| GTK (WebKitGTK on GLib ≥ 2.70) | ✅ | ✅ | ✅¹ | ✅¹ | ✅¹ |
| Android (Avalonia-aligned) | ✅ | ✅ | n/a² | n/a² | n/a² |
| Apple (1.6.x) | ✅ | ✅ | n/a³ | n/a³ | n/a³ |
| Mock | ✅ | ✅ | ✅ | ✅ | ✅ |

¹ On GTK, subject, issuer, and validity require GLib ≥ 2.70; otherwise the adapter passes `null` / omitted validity (WebKitGTK TLS callback still supplies host and summary).

² Android uses the `OnReceivedError` / `WebResourceError` path (aligned with Avalonia.Controls.WebView); that surface does not expose the leaf certificate, so structured cert fields are intentionally omitted.

³ Apple 1.6.x keeps `map_nsurl_error_to_category` for SSL failures; structured cert metadata for WKWebView is scheduled with v2 Phase 4 and the C# shim replacement.

Windows parses host from `RequestUri` and normalizes certificate validity to UTC `DateTimeOffset` per the WebView2 interop note in the navigation SSL plan amendment.

### Evidence pointers

- `tests/Agibuild.Fulora.UnitTests/AdapterSslRejectionContract.cs`
- `tests/Agibuild.Fulora.UnitTests/MockSslRejectionContractTests.cs`
- `tests/Agibuild.Fulora.UnitTests/Windows/WindowsWebViewAdapterSslTests.cs`
- `tests/Agibuild.Fulora.UnitTests/Gtk/GtkWebViewAdapterSslTests.cs`
- `tests/Agibuild.Fulora.UnitTests/Security/DefaultNavigationSecurityHooksTests.cs`
- `tests/Agibuild.Fulora.UnitTests/Security/WebViewSslExceptionContextTests.cs`
