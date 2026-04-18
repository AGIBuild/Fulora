# P5 — Public API Capability Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Break the monolithic `IWebViewFeatures` grab-bag (10 methods + 8 events) and lift the preload-script members out of `IWebViewScript` into a set of small, single-responsibility capability interfaces, while keeping `IWebView` source-compatible for existing consumers (additive, v1.x minor bump).

**Architecture:** Follow the Interface Segregation Principle from the public API inward. Introduce **13 fine-grained capability interfaces** — **12 are composed into the rewritten `IWebViewFeatures`** (DevTools, Screenshot, Printing, Zoom, FindInPage, NativeHandle, Downloads, Permissions, ContextMenu, PopupWindows, ResourceInterception, LifecycleEvents) and **1 (`IWebViewPreloadScripts`) is composed into the rewritten `IWebViewScript`**. Each old member remains reachable through inheritance, so `IWebView` and its subtypes keep their exact signatures. Breaking changes (remove `TryGetCookieManager`/`TryGetCommandManager`, hoist `ChannelId`, etc.) are out of scope here and deferred to a future v2.0 plan.

**Tech Stack:** C# 12, .NET 10, public surface lives in `src/Agibuild.Fulora.Core/`. Existing XML doc generation, analyzer enforcement, and `[Experimental("AGWVxxxx")]` codes (001–005 already claimed) continue to apply.

---

## Design Principles (Locked Before Task 1)

1. **Additive-only in this plan.** Every existing `IWebView*` member keeps its exact signature. No `[Obsolete]` yet. The plan does not delete or move anything that downstream code already depends on.
2. **One domain, one interface.** "DevTools" is not mixed with "screenshot". "Downloads" is not mixed with "permissions". Each new interface has 1–3 members.
3. **`IWebView` stays the ergonomic default.** Users who want the full composite keep writing `IWebView`. Users who only need one capability depend on the narrow interface (`IWebViewDevTools`, etc.) — that is the opt-in ISP path this plan unlocks.
4. **`IWebViewFeatures` is not deleted** — it is rewritten as `interface IWebViewFeatures : IWebViewDevTools, IWebViewScreenshot, …`. Every current member is inherited, not removed. Zero signature drift.
5. **No runtime behavior change.** `WebViewCore`, `WebDialog`, and the Avalonia `WebView` control already implement every member; after the split they implement the same members through narrower interfaces without writing a single new method body.
6. **Experimental marking is preserved exactly.** A member keeps the `[Experimental(...)]` attribute it had on the original interface — no more, no less. Today none of the 10 methods nor 8 events on `IWebViewFeatures` carry `[Experimental]` at the interface-member level (only the *argument types* `WebResourceRequestedEventArgs` / `EnvironmentRequestedEventArgs` / `ICookieManager` carry it, and those are untouched by this plan), so no new interface adds an `[Experimental]` attribute. Graduation and marking decisions for these members are tracked separately in `docs/API_SURFACE_REVIEW.md`.

---

## File Structure

New files — one per capability interface, each in `src/Agibuild.Fulora.Core/`:

| File | Responsibility |
|---|---|
| `IWebViewDevTools.cs` | DevTools open/close/query |
| `IWebViewScreenshot.cs` | Full-page screenshot capture |
| `IWebViewPrinting.cs` | Print-to-PDF |
| `IWebViewZoom.cs` | Zoom factor get/set (the existing `ZoomFactorChanged` event stays on the concrete `WebViewCore` — hoisting it is a breaking change, deferred to v2.0) |
| `IWebViewFindInPage.cs` | Find/stop-find |
| `IWebViewPreloadScripts.cs` | Add/remove preload scripts |
| `IWebViewNativeHandle.cs` | Async native handle accessor |
| `IWebViewDownloads.cs` | `DownloadRequested` event |
| `IWebViewPermissions.cs` | `PermissionRequested` event |
| `IWebViewContextMenu.cs` | `ContextMenuRequested` event |
| `IWebViewPopupWindows.cs` | `NewWindowRequested` event |
| `IWebViewResourceInterception.cs` | `WebResourceRequested` + `EnvironmentRequested` events |
| `IWebViewLifecycleEvents.cs` | `AdapterCreated` + `AdapterDestroyed` events |

Modified files:

| File | Change |
|---|---|
| `src/Agibuild.Fulora.Core/WebViewInterfaces.cs` | Rewrite `IWebViewFeatures` as composition of new interfaces; move `IWebViewScript.AddPreloadScriptAsync/RemovePreloadScriptAsync` into `IWebViewPreloadScripts` via inheritance (source-compatible) |
| `docs/API_SURFACE_REVIEW.md` | Append new section documenting the split |
| `docs/MIGRATION_GUIDE.md` | Append "consuming a single capability" guidance |
| `Directory.Build.props` | Bump `<VersionPrefix>` from `1.5.11` to `1.6.0` (minor — purely additive) |

No modifications needed in `src/Agibuild.Fulora.Runtime/`, `src/Agibuild.Fulora.Avalonia/`, or `src/Agibuild.Fulora.Platforms/` — because `WebViewCore`, `WebDialog`, and `WebView` already implement every method/event on `IWebViewFeatures`. Rewriting `IWebViewFeatures` as an interface composition picks up those implementations automatically; no new method bodies or interface maps are required.

The **only** file under `tests/` that needs to change is `tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs`, which contains three reflection assertions hard-coded to `typeof(IWebViewFeatures)` and therefore must be retargeted to `typeof(IWebViewDevTools)` after the split. This is a single file / three lines, handled by **Task 15a** below. No other test is affected.

---

## Verification Strategy

TDD in its canonical "red/green" form does not apply cleanly to purely additive interface decomposition — there is no new behavior to specify. Equivalent safety is achieved by the following automatic proofs:

- **Compilation proves implementation:** `WebViewCore`, `WebDialog`, and Avalonia `WebView` all declare `IWebView` (or a subtype). If the `IWebViewFeatures` recomposition were wrong — any missing signature, different return type, different event type — the C# compiler refuses to build the solution. `dotnet build Agibuild.Fulora.sln` IS the red/green signal.
- **Existing test suite proves no behavior regression:** All 2270 unit tests + 207 automation integration tests exercise the concrete types through `IWebView` and assorted sub-interfaces.
- **Governance spot-check:** A small grep verifies the split happened as intended (`IWebViewFeatures` declaration is now `: IWebViewDevTools, IWebViewScreenshot, …`). Added in Task 16.

### Known reflection-based test migration

`tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs:13–30` currently asserts member presence with `typeof(IWebViewFeatures).GetMethod("OpenDevToolsAsync")`. After Task 14, the three DevTools methods are inherited from `IWebViewDevTools` rather than declared directly on `IWebViewFeatures`. Because `Type.GetMethod` on an *interface type* does **not** walk up base interfaces, those assertions would fail.

This is the only test in the suite that asserts *which interface declares* the member rather than the behavior itself. The fix is a one-line retarget to `typeof(IWebViewDevTools)`, captured as **Task 15a** below. It is a one-liner per assertion, keeps the test's spirit (the capability must still exist on the public API), and is more accurate post-split — consumers will depend on `IWebViewDevTools` directly.

If any other `IWebViewFeatures.GetMethod`-style assertion surfaces during the Task 15 run, treat it the same way: retarget to the correct capability interface in a follow-up mini-task. A pre-flight grep in Task 1 catches any surprise.

---

## Task 1 — Confirm Member Mapping

**Files:**
- Read-only: `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`

- [ ] **Step 1: Build the exact split table**

Compare current `IWebViewFeatures` (10 methods + 8 events) and `IWebViewScript` (3 methods) against the 13 target interfaces. The final mapping is:

| Current member | Currently on | Target interface |
|---|---|---|
| `OpenDevToolsAsync` / `CloseDevToolsAsync` / `IsDevToolsOpenAsync` | `IWebViewFeatures` | `IWebViewDevTools` |
| `CaptureScreenshotAsync` | `IWebViewFeatures` | `IWebViewScreenshot` |
| `PrintToPdfAsync` | `IWebViewFeatures` | `IWebViewPrinting` |
| `GetZoomFactorAsync` / `SetZoomFactorAsync` | `IWebViewFeatures` | `IWebViewZoom` |
| `FindInPageAsync` / `StopFindInPageAsync` | `IWebViewFeatures` | `IWebViewFindInPage` |
| `AddPreloadScriptAsync` / `RemovePreloadScriptAsync` | `IWebViewScript` | `IWebViewPreloadScripts` |
| `TryGetWebViewHandleAsync` | `IWebViewFeatures` | `IWebViewNativeHandle` |
| `DownloadRequested` event | `IWebViewFeatures` | `IWebViewDownloads` |
| `PermissionRequested` event | `IWebViewFeatures` | `IWebViewPermissions` |
| `ContextMenuRequested` event | `IWebViewFeatures` | `IWebViewContextMenu` |
| `NewWindowRequested` event | `IWebViewFeatures` | `IWebViewPopupWindows` |
| `WebResourceRequested` + `EnvironmentRequested` events | `IWebViewFeatures` | `IWebViewResourceInterception` |
| `AdapterCreated` + `AdapterDestroyed` events | `IWebViewFeatures` | `IWebViewLifecycleEvents` |

Kept in place (not split):
- `IWebViewNavigation` (clean, single domain) — unchanged
- `IWebViewScript.InvokeScriptAsync` — stays on `IWebViewScript` (preload is the only move out)
- `IWebViewBridge` — unchanged in this plan; cleanup deferred to v2.0

- [ ] **Step 2: Pre-flight grep for reflection assertions**

Run:
```bash
rg -n 'typeof\(IWebView(Features|Script)\)\.GetMethod' tests/
```
Expected: only the three assertions in `tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs` (lines 13, 16, 19, 28–30). Any additional hit means another test will break on Task 14 and must be migrated — add a matching Task 15b for it.

- [ ] **Step 3: Commit the design note**

Write nothing yet — this task is confirmation only. The table and the reflection-test inventory above are the locked design. Proceed to Task 2.

---

## Task 2 — `IWebViewDevTools`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewDevTools.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: open, close, and query the developer tools pane attached to a WebView.
/// Implemented by every production <see cref="IWebView"/> — inherited by
/// <see cref="IWebViewFeatures"/> so existing callers continue to work unchanged.
/// </summary>
public interface IWebViewDevTools
{
    /// <summary>Opens the developer tools pane for this WebView.</summary>
    Task OpenDevToolsAsync();

    /// <summary>Closes the developer tools pane if open; no-op otherwise.</summary>
    Task CloseDevToolsAsync();

    /// <summary>Returns <see langword="true"/> when the developer tools pane is currently open.</summary>
    Task<bool> IsDevToolsOpenAsync();
}
```

- [ ] **Step 2: Build to confirm the new interface compiles**

Run: `dotnet build src/Agibuild.Fulora.Core/Agibuild.Fulora.Core.csproj --nologo`
Expected: `Build succeeded. 0 Warning(s), 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewDevTools.cs
git commit -m "Add IWebViewDevTools capability interface (P5 additive)"
```

---

## Task 3 — `IWebViewScreenshot`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewScreenshot.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: capture a full-page PNG screenshot of the current WebView content.
/// </summary>
public interface IWebViewScreenshot
{
    /// <summary>Captures the current viewport as PNG bytes.</summary>
    Task<byte[]> CaptureScreenshotAsync();
}
```

- [ ] **Step 2: Build, then commit**

Run: `dotnet build src/Agibuild.Fulora.Core/Agibuild.Fulora.Core.csproj --nologo` (expect success)

```bash
git add src/Agibuild.Fulora.Core/IWebViewScreenshot.cs
git commit -m "Add IWebViewScreenshot capability interface (P5 additive)"
```

---

## Task 4 — `IWebViewPrinting`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewPrinting.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: render the current page to a PDF byte stream.
/// </summary>
public interface IWebViewPrinting
{
    /// <summary>
    /// Prints the current page to PDF. When <paramref name="options"/> is
    /// <see langword="null"/> the adapter's platform defaults apply.
    /// </summary>
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null);
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewPrinting.cs
git commit -m "Add IWebViewPrinting capability interface (P5 additive)"
```

---

## Task 5 — `IWebViewZoom`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewZoom.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: read and adjust the page zoom factor. The zoom factor is clamped
/// to the range [0.25, 5.0]; values outside that range are silently adjusted.
/// </summary>
public interface IWebViewZoom
{
    /// <summary>Gets the current zoom factor (1.0 = 100%).</summary>
    Task<double> GetZoomFactorAsync();

    /// <summary>Sets the zoom factor (clamped to [0.25, 5.0]).</summary>
    Task SetZoomFactorAsync(double zoomFactor);
}
```

Note: `ZoomFactorChanged` is intentionally NOT added to this interface in this plan — the event currently lives on the concrete `WebViewCore` type (not on `IWebView*`) and exposing it would be a behavioral surface change. Document this deferral; hoisting the event is a separate v2.0 item.

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewZoom.cs
git commit -m "Add IWebViewZoom capability interface (P5 additive)"
```

---

## Task 6 — `IWebViewFindInPage`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewFindInPage.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: incremental find-in-page search with highlight management.
/// </summary>
public interface IWebViewFindInPage
{
    /// <summary>
    /// Searches the current page for <paramref name="text"/>. Returns match count
    /// and active match index.
    /// </summary>
    Task<FindInPageEventArgs> FindInPageAsync(string text, FindInPageOptions? options = null);

    /// <summary>
    /// Clears find-in-page highlights and resets search state.
    /// </summary>
    Task StopFindInPageAsync(bool clearHighlights = true);
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewFindInPage.cs
git commit -m "Add IWebViewFindInPage capability interface (P5 additive)"
```

---

## Task 7 — `IWebViewPreloadScripts`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewPreloadScripts.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: register and remove JavaScript snippets that run at document start
/// on every navigation.
/// </summary>
public interface IWebViewPreloadScripts
{
    /// <summary>
    /// Registers a preload script. The returned opaque ID can be passed to
    /// <see cref="RemovePreloadScriptAsync"/> to unregister it.
    /// </summary>
    Task<string> AddPreloadScriptAsync(string javaScript);

    /// <summary>Removes a previously registered preload script by ID.</summary>
    Task RemovePreloadScriptAsync(string scriptId);
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewPreloadScripts.cs
git commit -m "Add IWebViewPreloadScripts capability interface (P5 additive)"
```

---

## Task 8 — `IWebViewNativeHandle`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewNativeHandle.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: retrieve the underlying platform-native WebView handle. The handle
/// is only valid between <c>AdapterCreated</c> and <c>AdapterDestroyed</c>.
/// </summary>
public interface IWebViewNativeHandle
{
    /// <summary>
    /// Asynchronously retrieves the native platform WebView handle, or
    /// <see langword="null"/> when the adapter has been destroyed.
    /// </summary>
    Task<INativeHandle?> TryGetWebViewHandleAsync();
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewNativeHandle.cs
git commit -m "Add IWebViewNativeHandle capability interface (P5 additive)"
```

---

## Task 9 — `IWebViewDownloads`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewDownloads.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe download requests initiated from page content. Handlers
/// may cancel or redirect downloads via <see cref="DownloadRequestedEventArgs"/>.
/// </summary>
public interface IWebViewDownloads
{
    /// <summary>
    /// Raised when the page initiates a download. Handlers run on the UI thread.
    /// </summary>
    event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewDownloads.cs
git commit -m "Add IWebViewDownloads capability interface (P5 additive)"
```

---

## Task 10 — `IWebViewPermissions`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewPermissions.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe runtime permission prompts (camera, microphone, geolocation, etc.).
/// </summary>
public interface IWebViewPermissions
{
    /// <summary>
    /// Raised when page content requests a platform permission. Handlers must grant
    /// or deny via <see cref="PermissionRequestedEventArgs"/> before the handler returns.
    /// </summary>
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewPermissions.cs
git commit -m "Add IWebViewPermissions capability interface (P5 additive)"
```

---

## Task 11 — `IWebViewContextMenu`, `IWebViewPopupWindows`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewContextMenu.cs`
- Create: `src/Agibuild.Fulora.Core/IWebViewPopupWindows.cs`

Grouped into one task because both are single-event capability wrappers.

- [ ] **Step 1: Create `IWebViewContextMenu.cs`**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe and optionally suppress the platform context menu
/// (right-click, long-press).
/// </summary>
public interface IWebViewContextMenu
{
    /// <summary>
    /// Raised when the user triggers a context menu. Setting
    /// <see cref="ContextMenuRequestedEventArgs.Handled"/> suppresses the default menu.
    /// </summary>
    event EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;
}
```

- [ ] **Step 2: Create `IWebViewPopupWindows.cs`**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe requests to open a new window (target="_blank", window.open).
/// When unhandled, the navigation falls back to in-place in the current WebView.
/// </summary>
public interface IWebViewPopupWindows
{
    /// <summary>
    /// Raised when page content requests a new window. Set
    /// <see cref="NewWindowRequestedEventArgs.Handled"/> to claim the navigation.
    /// </summary>
    event EventHandler<NewWindowRequestedEventArgs>? NewWindowRequested;
}
```

- [ ] **Step 3: Build, then commit both**

```bash
git add src/Agibuild.Fulora.Core/IWebViewContextMenu.cs src/Agibuild.Fulora.Core/IWebViewPopupWindows.cs
git commit -m "Add IWebViewContextMenu and IWebViewPopupWindows capability interfaces (P5 additive)"
```

---

## Task 12 — `IWebViewResourceInterception`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewResourceInterception.cs`

- [ ] **Step 1: Create the interface file**

Two events live in one interface because they represent the same domain (intercepting outbound HTTP before the adapter makes the call).

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: intercept and inspect outbound HTTP(S) requests before the adapter
/// issues them, and observe environment-initialization events.
/// </summary>
/// <remarks>
/// Experimental-API markers on the event argument types
/// (<c>WebResourceRequestedEventArgs</c>, <c>EnvironmentRequestedEventArgs</c>)
/// continue to apply to consumers; see <c>docs/API_SURFACE_REVIEW.md</c>. No
/// attribute is added at the event level because the original members on
/// <c>IWebViewFeatures</c> carry none.
/// </remarks>
public interface IWebViewResourceInterception
{
    /// <summary>
    /// Raised for every outbound resource request. Handlers may substitute the
    /// response via <c>WebResourceRequestedEventArgs</c>.
    /// </summary>
    event EventHandler<WebResourceRequestedEventArgs>? WebResourceRequested;

    /// <summary>
    /// Raised once per adapter to allow late binding of the environment options
    /// before the native WebView process starts.
    /// </summary>
    event EventHandler<EnvironmentRequestedEventArgs>? EnvironmentRequested;
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewResourceInterception.cs
git commit -m "Add IWebViewResourceInterception capability interface (P5 additive)"
```

---

## Task 13 — `IWebViewLifecycleEvents`

**Files:**
- Create: `src/Agibuild.Fulora.Core/IWebViewLifecycleEvents.cs`

- [ ] **Step 1: Create the interface file**

```csharp
namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe the creation and destruction of the underlying platform
/// adapter. Each WebView lifecycle raises <c>AdapterCreated</c> exactly once
/// and <c>AdapterDestroyed</c> at most once before disposal.
/// </summary>
public interface IWebViewLifecycleEvents
{
    /// <summary>
    /// Raised after the native adapter is attached and ready for navigation.
    /// </summary>
    event EventHandler<AdapterCreatedEventArgs>? AdapterCreated;

    /// <summary>
    /// Raised exactly once as the adapter is being torn down. Subscribers must
    /// not enqueue new work on the WebView from the handler body.
    /// </summary>
    event EventHandler? AdapterDestroyed;
}
```

- [ ] **Step 2: Build, then commit**

```bash
git add src/Agibuild.Fulora.Core/IWebViewLifecycleEvents.cs
git commit -m "Add IWebViewLifecycleEvents capability interface (P5 additive)"
```

---

## Task 14 — Recompose `IWebViewFeatures` and `IWebViewScript`

**Files:**
- Modify: `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`

This is the task that "flips the switch". After this change the 12 new interfaces become reachable from `IWebView`, and users can opt-in to one without pulling the rest.

- [ ] **Step 1: Rewrite `IWebViewScript` to inherit `IWebViewPreloadScripts`**

Replace the existing block:

```csharp
/// <summary>JavaScript execution and preload script management.</summary>
public interface IWebViewScript
{
    Task<string?> InvokeScriptAsync(string script);
    Task<string> AddPreloadScriptAsync(string javaScript);
    Task RemovePreloadScriptAsync(string scriptId);
}
```

with:

```csharp
/// <summary>
/// JavaScript execution plus the preload-script capability
/// (<see cref="IWebViewPreloadScripts"/>). Kept as a composite for source-level
/// compatibility — existing consumers of <c>IWebViewScript</c> continue to see
/// all three members unchanged.
/// </summary>
public interface IWebViewScript : IWebViewPreloadScripts
{
    Task<string?> InvokeScriptAsync(string script);
}
```

- [ ] **Step 2: Rewrite `IWebViewFeatures` as an interface composition**

Replace the existing `IWebViewFeatures` block with:

```csharp
/// <summary>
/// Composite "extended capabilities" interface. Derives every member from a
/// single-responsibility capability interface so new code can depend on the
/// narrowest contract (e.g. <see cref="IWebViewDevTools"/>) while legacy code
/// keeps depending on <see cref="IWebViewFeatures"/> unchanged.
/// </summary>
public interface IWebViewFeatures :
    IWebViewDevTools,
    IWebViewScreenshot,
    IWebViewPrinting,
    IWebViewZoom,
    IWebViewFindInPage,
    IWebViewNativeHandle,
    IWebViewDownloads,
    IWebViewPermissions,
    IWebViewContextMenu,
    IWebViewPopupWindows,
    IWebViewResourceInterception,
    IWebViewLifecycleEvents
{
}
```

**Important:** remove every member body from `IWebViewFeatures` — they are all inherited now. The only reason to leave the interface declaration is to preserve source compatibility for existing `IWebViewFeatures x` consumers.

- [ ] **Step 3: Build the Core project**

Run: `dotnet build src/Agibuild.Fulora.Core/Agibuild.Fulora.Core.csproj --nologo`
Expected: `0 Warning(s), 0 Error(s)`. If there is a signature mismatch (e.g. optional parameter default or event delegate type), fix it before continuing.

- [ ] **Step 4: Build the whole solution (except Xcode-dependent iOS/macOS native projects)**

Run:
```bash
dotnet build src/Agibuild.Fulora.Runtime/Agibuild.Fulora.Runtime.csproj --nologo \
  && dotnet build src/Agibuild.Fulora.Avalonia/Agibuild.Fulora.Avalonia.csproj --nologo \
  && dotnet build tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --nologo \
  && dotnet build tests/Agibuild.Fulora.Integration.Tests.Automation/Agibuild.Fulora.Integration.Tests.Automation.csproj --nologo
```
Expected: every project reports `0 Error(s)`. Any failure means a concrete type (`WebViewCore`, `WebView`, `WebDialog`, stubs) no longer matches a member that moved — read the error message and align the signature (this should not happen if Task 1's table is correct).

- [ ] **Step 5: Commit the recomposition**

```bash
git add src/Agibuild.Fulora.Core/WebViewInterfaces.cs
git commit -m "Recompose IWebViewFeatures / IWebViewScript via capability interfaces (P5 additive)"
```

---

## Task 15 — Full-Suite Verification

**Files:** (none modified)

- [ ] **Step 1: Run unit tests — expect 3 failures**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --no-build --nologo`
Expected: **3 failures** in `DevToolsTests`. Specifically:

- `DevToolsTests.IWebView_declares_async_DevTools_members`
- `DevToolsTests.IDevToolsAdapter_interface_has_expected_members`

These fail because `typeof(IWebViewFeatures).GetMethod("OpenDevToolsAsync")` now returns `null` (methods are inherited from `IWebViewDevTools`, and `Type.GetMethod` on interfaces does not walk base interfaces). Task 15a fixes them.

Every other test in the 2270-test suite must still pass. If a different test fails, **stop** and investigate — something else is wrong with the split.

- [ ] **Step 2: Run automation integration tests**

Run: `dotnet test tests/Agibuild.Fulora.Integration.Tests.Automation/Agibuild.Fulora.Integration.Tests.Automation.csproj --no-build --nologo`
Expected: `Passed! Failed: 0, Passed: 207, Skipped: 0, Total: 207`. Integration tests do not use this reflection pattern, so they should be green.

- [ ] **Step 3: No commit**

Proceed to Task 15a to fix the expected DevToolsTests failures.

---

## Task 15a — Migrate `DevToolsTests` Reflection Assertions

**Files:**
- Modify: `tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs:11,13,16,19,27,28,29,30`

- [ ] **Step 1: Retarget reflection to the new capability interface**

Open `tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs`. Apply this minimal replacement inside the first two `[Fact]` methods:

```csharp
[Fact]
public void IWebView_declares_async_DevTools_members()
{
    Assert.True(typeof(IWebViewDevTools).IsAssignableFrom(typeof(IWebView)));

    var methods = typeof(IWebViewDevTools).GetMethod("OpenDevToolsAsync");
    Assert.NotNull(methods);

    methods = typeof(IWebViewDevTools).GetMethod("CloseDevToolsAsync");
    Assert.NotNull(methods);

    methods = typeof(IWebViewDevTools).GetMethod("IsDevToolsOpenAsync");
    Assert.NotNull(methods);
    Assert.Equal(typeof(Task<bool>), methods!.ReturnType);
}

[Fact]
public void IDevToolsAdapter_interface_has_expected_members()
{
    Assert.True(typeof(IWebViewDevTools).IsAssignableFrom(typeof(IWebView)));
    Assert.NotNull(typeof(IWebViewDevTools).GetMethod("OpenDevToolsAsync"));
    Assert.NotNull(typeof(IWebViewDevTools).GetMethod("CloseDevToolsAsync"));
    Assert.NotNull(typeof(IWebViewDevTools).GetMethod("IsDevToolsOpenAsync"));
}
```

Do **not** touch the third `[Fact]` (`TestWebViewHost_DevTools_are_noop`) — it is a behavior test that goes through the concrete host and is unaffected.

- [ ] **Step 2: Rerun the unit suite**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --no-build --nologo`
Expected: `Passed! Failed: 0, Passed: 2270, Skipped: 0, Total: 2270`.

- [ ] **Step 3: Commit**

```bash
git add tests/Agibuild.Fulora.UnitTests/DevToolsTests.cs
git commit -m "Retarget DevToolsTests reflection to IWebViewDevTools (P5 follow-up)"
```

---

## Task 16 — Governance Spot-Check

**Files:** (none modified — verification only)

- [ ] **Step 1: Verify `IWebViewFeatures` now uses composition**

Run:
```bash
rg -n 'public interface IWebViewFeatures' src/Agibuild.Fulora.Core/WebViewInterfaces.cs
```
Expected: a single line ending with `IWebViewLifecycleEvents`. Confirms the grab-bag is gone.

- [ ] **Step 2: Verify no member leaked out**

Run:
```bash
rg -cE '(OpenDevToolsAsync|CaptureScreenshotAsync|PrintToPdfAsync|GetZoomFactorAsync|SetZoomFactorAsync|FindInPageAsync|StopFindInPageAsync|AddPreloadScriptAsync|RemovePreloadScriptAsync|TryGetWebViewHandleAsync)' src/Agibuild.Fulora.Core/WebViewInterfaces.cs
```
Expected: `0` — every method has moved to its capability interface and exists in `WebViewInterfaces.cs` only via inheritance.

- [ ] **Step 3: Verify all 13 new interfaces exist**

Run:
```bash
ls src/Agibuild.Fulora.Core/IWebView{DevTools,Screenshot,Printing,Zoom,FindInPage,PreloadScripts,NativeHandle,Downloads,Permissions,ContextMenu,PopupWindows,ResourceInterception,LifecycleEvents}.cs
```
Expected: all 13 paths (12 composed into `IWebViewFeatures` + `IWebViewPreloadScripts` composed into `IWebViewScript`) print without errors. (Missing files cause `ls: ... No such file`.)

- [ ] **Step 4: Commit (no-op — verification only)**

Nothing to commit. Proceed to Task 17.

---

## Task 17 — Documentation: API Surface Review

**Files:**
- Modify: `docs/API_SURFACE_REVIEW.md`

- [ ] **Step 1: Append a new section**

Append the following at the end of the file:

```markdown

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
```

- [ ] **Step 2: Commit**

```bash
git add docs/API_SURFACE_REVIEW.md
git commit -m "Document P5 capability-split additions in API Surface Review"
```

---

## Task 18 — Documentation: Migration Guide

**Files:**
- Modify: `docs/MIGRATION_GUIDE.md`

- [ ] **Step 1: Append a new section**

Append at end of file:

```markdown

## 1.5.x → 1.6.0 (P5: Capability Split)

**Nothing is required.** All changes are additive.

### What's new

You can now depend on a single WebView capability instead of the whole `IWebView` composite. This is especially useful for:

- **Testing**: stub only the interface you consume, e.g. `Mock<IWebViewZoom>` instead of full `IWebView`
- **Dependency injection**: register components against narrow contracts (`IWebViewDevTools`, `IWebViewScreenshot`, ...) so substitution works per-capability
- **Library authors**: expose capabilities on downstream types without implying they own the whole WebView surface

### Example

Before:

\```csharp
public sealed class PdfExporter
{
    private readonly IWebView _webView; // overfits — we only need printing
    public PdfExporter(IWebView webView) => _webView = webView;
    public Task<byte[]> ExportAsync() => _webView.PrintToPdfAsync();
}
\```

After (recommended for new code):

\```csharp
public sealed class PdfExporter
{
    private readonly IWebViewPrinting _printing;
    public PdfExporter(IWebViewPrinting printing) => _printing = printing;
    public Task<byte[]> ExportAsync() => _printing.PrintToPdfAsync();
}
\```

Both forms compile against the same `WebViewCore` / `WebDialog` / `WebView` instances — no implementation change is needed downstream.
```

Note: the `\``` ` escapes are for the plan document only — in the real markdown file, use unescaped triple backticks.

- [ ] **Step 2: Commit**

```bash
git add docs/MIGRATION_GUIDE.md
git commit -m "Document P5 capability-interface opt-in pattern in Migration Guide"
```

---

## Task 19 — Version Bump

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Bump minor version**

Find the line `<VersionPrefix>1.5.11</VersionPrefix>` and change it to `<VersionPrefix>1.6.0</VersionPrefix>`. The new interfaces are additive, so minor-version bump (semver) is correct.

- [ ] **Step 2: Build whole solution to confirm the bump did not break anything**

Run: `dotnet build Agibuild.Fulora.sln --nologo 2>&1 | tail -5`
Expected: Runtime / Avalonia / Core / UnitTests / Automation projects build cleanly. iOS and macOS native-shim projects may still fail because of local Xcode toolchain limits — ignore those, they are not affected by this change.

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "Bump version to 1.6.0 (P5 capability split — additive)"
```

---

## Task 20 — Final Verification & Summary Commit

**Files:** (none modified — final check)

- [ ] **Step 1: Re-run both test suites to confirm green**

Run:
```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --no-build --nologo \
  && dotnet test tests/Agibuild.Fulora.Integration.Tests.Automation/Agibuild.Fulora.Integration.Tests.Automation.csproj --no-build --nologo
```
Expected: 2270 + 207 tests all pass.

- [ ] **Step 2: Review the full commit series**

Run: `git log --oneline $(git merge-base HEAD origin/main)..HEAD`
Expected: **17 commits** on top of the merge-base, linear, each message starts with an action verb. Count sources:

- Tasks 2, 3, 4, 5, 6, 7, 8, 9, 10 → 9 commits (one per task, one interface each)
- Task 11 → 1 commit (two interface files, single commit)
- Tasks 12, 13 → 2 commits
- Task 14 (recomposition) → 1 commit
- Task 15 → 0 commits (red test run only)
- Task 15a (DevToolsTests migration) → 1 commit
- Task 16 → 0 commits (governance grep only)
- Task 17 (API Surface Review) → 1 commit
- Task 18 (Migration Guide) → 1 commit
- Task 19 (version bump) → 1 commit

Total: 9 + 1 + 2 + 1 + 1 + 1 + 1 + 1 = **17 commits**.

- [ ] **Step 3: No separate summary commit**

This plan follows the convention that each task commits its own artefact; there is no wrap-up commit. Work ends here.

---

## Out of Scope — Tracked for a Future v2.0 Plan

The following cleanups were considered and **deliberately excluded** from this plan because they are source-breaking. They are listed here so they are not forgotten.

1. **Remove `TryGetCookieManager` / `TryGetCommandManager`.** P0 made cookie/command support mandatory on every adapter, so these `Try-Get-null` methods are misleading — they never return null in production. Replace with direct properties `ICookieManager Cookies { get; }` and `ICommandManager Commands { get; }` on a new `IWebViewStorage` / `IWebViewEditing` interface, and obsolete the old methods.
2. **Hoist `ChannelId` out of `IWebViewBridge`.** `ChannelId` is WebView identity, not a bridge concept. Move to the root `IWebView` interface.
3. **Hoist `ZoomFactorChanged` onto `IWebViewZoom`.** Today it exists only on the concrete `WebViewCore`. Adding an event to a public interface is technically breaking (existing implementers would need to add the member).
4. **Retire the empty `IWebViewFeatures` composite** once all downstream code has migrated to the narrow interfaces. Mark it `[Obsolete]` in 1.7, delete in 2.0.
5. **Mark `IWebViewResourceInterception.EnvironmentRequested` non-experimental** once the environment-request story has a concrete implementation on every adapter (currently `[Experimental("AGWV005")]`).

Each of those is a separate OpenSpec change with its own deprecation window.
