# Fulora v2.0 Public API Breakage Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task once it is approved. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Planning. No code changes until this plan is accepted and v2.0 branch is carved.
**Goal:** Retire the vestigial null-returning "TryGet*" manager accessors, promote platform-independent state (`ChannelId`), lift `EnableSpaHosting` into the base `IWebView` contract where safe, and resolve the two `[Experimental]` attribute holdovers (`AGWV001`, `AGWV005`).
**Architecture:** Keep each breaking change behind a narrow task with an explicit migration path, a SemVer evidence requirement, and a rollback strategy. Do not bundle unrelated breakage. Every task must land in a single `release-gate-required` commit with a capability registry update and a CHANGELOG entry. Preserve runtime behavior — the public API shape changes, the implementation does not.
**Tech Stack:** .NET 10, C# 12, existing `Agibuild.Fulora.Core` public surface, `Directory.Build.props` VersionPrefix, `framework-capabilities.json`, `docs/API_SURFACE_REVIEW.md`, `docs/MIGRATION_GUIDE.md`.

---

## Scope Guardrails

- Do not include any new feature work in v2.0. This plan is removal + promotion only.
- Do not change wire formats (bridge transport, RPC envelope). Those are Tier A Bridge contracts and live under `bridge.binary` / `bridge.cancellation` in the capability registry with their own `release-gate-required` policy.
- Do not migrate to a new TFM or a new Avalonia major version inside the same release; bundling transitive ecosystem breakage with public API breakage hides the blast radius.
- Do not rename types to "look cleaner". Only rename where the old name actively misleads callers (e.g. `TryGet*` that can never return null).
- Do not mark or unmark `[Experimental]` without either a behavior change or a graduation evidence record.
- Do not remove adapter-level internal plumbing as part of the "retirement" of a public API — those are two separate concerns.

## Release Policy for v2.0

- Branch: `release/v2` carved from `main` at the start of Task 1; tags on that branch use `2.0.0-preview.*` until graduation.
- Every task in this plan lands as one commit with:
  - the code change
  - the `framework-capabilities.json` update if the capability status changes
  - a row in `docs/MIGRATION_GUIDE.md`
  - a row in `CHANGELOG.md` under a dedicated `## [2.0.0]` heading
  - an entry in `docs/API_SURFACE_REVIEW.md` under a new "2.0 Breakage Audit" section
- `release-gate` job in `.github/workflows/ci.yml` must remain green; v2 must also pass the existing governance test suites (`Agibuild.Fulora.Governance.UnitTests`, `Agibuild.Fulora.Docs.UnitTests`).
- Any task that breaks the public API inventory (`docs/API_SURFACE_INVENTORY.release.txt`) must land with the regenerated inventory in the same commit.

## Expected Version Cadence

- `2.0.0-preview.1` → finish Task 1 + Task 2 on a feature branch. Tag for early adopter feedback.
- `2.0.0-preview.2` → finish Task 3 + Task 4 + Task 5.
- `2.0.0-rc.1` → Task 6 (migration doc) + full regression.
- `2.0.0` → promote after at least one full release cycle on `2.0.0-rc.1` with no critical regression reports.

---

## Rejected Alternatives

| Alternative | Rejection reason |
|---|---|
| Ship one monolithic v2.0 that also drops .NET 10 support | Bundles SDK churn with API churn; impossible to attribute regressions. |
| Land each breakage as a `1.x` minor under `[Experimental]` first | These members have never been `[Experimental]`. Making them experimental *now* to delete them *later* is SemVer kabuki, not an honest deprecation path. |
| Ship a compatibility shim package `Agibuild.Fulora.LegacyApi` that re-exposes the retired members | Keeps the dependency graph confusing forever; refuses to commit to the v2 contract. Reject. |
| Replace `TryGet*Manager()` with DI-injected managers from IServiceProvider | Spreads a cross-cutting concern (bridge capabilities) into DI scope management; adapters do not own a scope. Current direct property access is architecturally correct — the fix is the **name**, not the lookup mechanism. |

## Architectural Invariants

- `IWebView` remains the composite contract. Every capability visible on an Avalonia `WebView` must be discoverable through `IWebView` in v2 without downcasting.
- `WebViewCore`, `WebDialog`, and Avalonia `WebView` must all implement the same contract — no host-specific feature gaps introduced by this plan.
- `[Experimental]` must mean "the shape of the API may change"; once the shape is final, the attribute is removed. An unimplemented placeholder (`AGWV005 EnvironmentRequestedEventArgs`) does not earn the attribute — it earns a decision: implement or delete.
- Every removed member must be reachable through a one-line mechanical migration. If the migration requires a design decision from the consumer, the removal is not safe yet.

---

## Task 1 — Retire `TryGetCookieManager()` and `TryGetCommandManager()`

**Rationale.** `WebViewCore` constructs `_cookieManager` and `_commandManager` unconditionally via `WebViewCoreCapabilityDetectionRuntime.CreateCookieManager()` / `CreateCommandManager()`. Both `TryGet*()` methods **always return non-null**. The `TryGet*` prefix + `?` suffix are documentation lies that force every caller into a null check that cannot fail. Depending on `ICookieManager? TryGetCookieManager()` is strictly worse than depending on `ICookieManager CookieManager { get; }` on `IWebViewBridge`.

**Files to modify:**

- `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`
  - Replace `ICookieManager? TryGetCookieManager()` with `ICookieManager Cookies { get; }` on `IWebViewBridge`.
  - Replace `ICommandManager? TryGetCommandManager()` with `ICommandManager Commands { get; }` on `IWebViewBridge`.
- `src/Agibuild.Fulora.Runtime/WebViewCore.cs`
  - Replace method with get-only property backed by `_cookieManager` / `_commandManager`.
- `src/Agibuild.Fulora.Runtime/WebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebView.cs`, `src/Agibuild.Fulora.Avalonia/AvaloniaWebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebViewControlRuntime.cs`
  - Forward the property; delete the method.
- `src/Agibuild.Fulora.Runtime/Shell/ShellBrowserInteractionRuntime.cs`, `src/Agibuild.Fulora.Avalonia/WebViewShortcutRouter.cs`
  - Replace the two internal consumers with direct property access; drop their null-path branches (which today are dead code — non-null guaranteed).
- `tests/Agibuild.Fulora.UnitTests/ContractSemanticsV1CookieTests.cs`, `tests/Agibuild.Fulora.Integration.Tests.Automation/CookieIntegrationTests.cs`, `tests/Agibuild.Fulora.UnitTests/CookieCoverageTests.cs`
  - Retarget to `.Cookies` / `.Commands`.

**Migration for consumers:** `webView.TryGetCookieManager()!` → `webView.Cookies`. Identical for commands.

**Acceptance:**

- Public API inventory diff shows **only** these members added/removed. No unrelated churn.
- `Agibuild.Fulora.UnitTests`, `Agibuild.Fulora.Integration.Tests.Automation`, and `Agibuild.Fulora.Governance.UnitTests` pass without a single `!` null-forgiving operator retained against the new properties.

---

## Task 2 — Promote `ChannelId` from `IWebViewBridge` to `IWebView`

**Rationale.** Every consumer of `ChannelId` already goes through an `IWebView` reference (bridge tracers, diagnostics sinks, E2E test helpers). Keeping `ChannelId` buried on `IWebViewBridge` forces consumers to depend on the bridge sub-interface even when the channel identity is a view-level concept (one `IWebView` ⇔ one channel for its lifetime). The internal `IWebViewAdapterHost.ChannelId` is already duplicated at the top level anyway.

**Files to modify:**

- `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`
  - Move `Guid ChannelId { get; }` from `IWebViewBridge` to `IWebView`.
  - Keep `IWebViewBridge` focused on bridge primitives (`Rpc`, `BridgeTracer`, `Bridge`, `WebMessageReceived`).
- `src/Agibuild.Fulora.Runtime/WebViewCore.cs`, `src/Agibuild.Fulora.Runtime/WebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebView.cs`, `src/Agibuild.Fulora.Avalonia/AvaloniaWebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebViewControlRuntime.cs`
  - Relocate the property without changing its backing field.
- Governance: `tests/Agibuild.Fulora.Governance.UnitTests/`
  - Extend the inventory snapshot test to prove `IWebView.ChannelId` exists and `IWebViewBridge.ChannelId` does not.

**Acceptance:**

- `IWebViewBridge` no longer declares `ChannelId`.
- Any code that previously wrote `bridge.ChannelId` continues to compile only if `bridge` is typed as `IWebView` (or a subtype), not `IWebViewBridge`.

---

## Task 3 — Lift `EnableSpaHosting` into `IWebView`

**Rationale.** `ISpaHostingWebView : IWebView` exists only because 1.x did not want to break the `IWebView` contract. `WebViewCore`, `WebDialog`, and the Avalonia `WebView` control *all three* already implement `ISpaHostingWebView` in 1.6.x. There is no implementation that exposes `IWebView` without also supporting SPA hosting — the "optional" marker interface protects nothing. Lifting the method up removes a downcast and retires a single-consumer interface.

**Files to modify:**

- `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`
  - Add `void EnableSpaHosting(SpaHostingOptions options);` to `IWebView` directly.
  - Delete `ISpaHostingWebView`.
- `src/Agibuild.Fulora.Runtime/WebViewCore.cs`, `src/Agibuild.Fulora.Runtime/WebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebView.cs`, `src/Agibuild.Fulora.Avalonia/AvaloniaWebDialog.cs`, `src/Agibuild.Fulora.Avalonia/WebViewControlRuntime.cs`
  - Remove the `ISpaHostingWebView` from the class declaration; the method body stays.
- `src/Agibuild.Fulora.Runtime/SpaHostingExtensions.cs`
  - If extension methods target `ISpaHostingWebView`, retarget to `IWebView` and delete the now-dead overloads.
- `docs/articles/spa-hosting.md`
  - Update the narrative to stop saying "downcast to `ISpaHostingWebView`".

**Acceptance:**

- No public type named `ISpaHostingWebView` in the 2.0 inventory.
- `SpaHostingExtensions` public surface compiles against `IWebView` only.
- `docs/articles/spa-hosting.md` references `IWebView` throughout.

---

## Task 4 — Resolve `AGWV001` on `ICookieManager`

**Rationale.** The attribute was added when Android's `CookieManager` integration was incomplete. As of 1.6.x, all five adapters (Windows/macOS/iOS/Android/Gtk) implement `ICookieAdapter`. The remaining open question is cross-platform behavioral parity, not platform coverage. The 1.6.x-line spec-follow-up (this doc's sibling "Task 4 — AGWV001 Cookie Manager 跨平台补齐") adds the shared contract test fixture that produces graduation evidence.

**Precondition for this task (must be true before this task runs):**

- The shared `AdapterCookieContract` test fixture from the 1.6.x follow-up work exists and is green against every platform adapter target that CI runs (Windows WebView2, macOS WKWebView, Linux WebKitGtk; iOS/Android run in platform integration lanes).
- `docs/API_SURFACE_REVIEW.md` reflects the actual Android adapter status (no more "returns null") — already delivered by 1.6.x follow-up.

**Files to modify:**

- `src/Agibuild.Fulora.Core/WebViewInterfaces.cs`
  - Remove `[Experimental("AGWV001")]` from `ICookieManager`.
- `docs/API_SURFACE_REVIEW.md`
  - Move AGWV001 from "1.0 Freeze Status" to "2.0 Graduations" with evidence link.
- `docs/framework-capabilities.json`
  - If a `cookies` capability entry is still absent, add it with `status: stable`, `supportTier: Tier A`, `owner: runtime-core`, `breakingChangePolicy: release-gate-required`.

**Acceptance:**

- No `[Experimental("AGWV001")]` remains in the public surface.
- Graduation evidence is a direct link to the shared-contract test run on the release tag.

---

## Task 5 — Decide `AGWV005` on `EnvironmentRequestedEventArgs`

**Rationale.** The type is a placeholder — no adapter raises the event in 1.6.x, no test asserts on it. An `[Experimental]` attribute on a type that has no implementation is a code smell: either the type is a contract (implement + graduate), or it is speculative (remove).

**Decision required before this task starts:**

- (A) Commit to an implementation. If so, Task 5 becomes a design task, not a v2.0 removal. In that case defer Task 5 out of v2.0 entirely and track it under a new capability entry (`environment.requested`) with provisional status.
- (B) Remove the type. Callers cannot possibly consume it today. Removing it is a net reduction in public surface.

**Default proposal:** (B). If no owner claims the feature during v2.0 planning, delete it.

**Files to modify (assuming (B)):**

- `src/Agibuild.Fulora.Core/WebViewRecords.cs`
  - Delete the `EnvironmentRequestedEventArgs` record and the `AGWV005` attribute usage.
- Any `EnvironmentRequested` event declaration on `IWebViewResourceInterception` or similar
  - Remove.
- `docs/API_SURFACE_REVIEW.md`
  - Move AGWV005 to "2.0 Removals" with rationale.

**Acceptance:**

- No `AGWV005` identifier anywhere in the repo.
- `docs/MIGRATION_GUIDE.md` documents the removal with a one-liner ("If you subscribed to `EnvironmentRequested`, the event was never raised by any adapter in 1.x — no replacement needed").

---

## Task 6 — Migration Guide and Evidence Bundle

**Rationale.** Every v2.0 adopter must be able to mechanically migrate. A single `docs/MIGRATION_GUIDE.md` section for "1.6.x → 2.0.0" is the only contract that matters from their side. Everything else is bookkeeping.

**Files to modify:**

- `docs/MIGRATION_GUIDE.md`
  - Append a `## 1.6.x → 2.0.0` section listing, per task:
    - exact symbol removed
    - exact replacement symbol (or "removed, no replacement needed")
    - one-line sample
- `docs/API_SURFACE_REVIEW.md`
  - Add a `## 2.0 Breakage Audit` section with the final inventory diff summary.
- `CHANGELOG.md`
  - `## [2.0.0] — YYYY-MM-DD` section with subheadings `### Removed`, `### Promoted`, `### Graduated`, referencing each task.

**Acceptance:**

- A developer holding a 1.6.x codebase can perform the migration with `rg` + sed and compile.
- Governance suite parses the new sections (the existing `DocumentationGovernanceTests` is already enforcing structure; if it does not yet check a v2 section, add a single assertion).

---

## Stop Conditions

Stop v2.0 work and re-plan when any of these become true:

- A removed member has an active consumer in the Fulora template / sample apps that cannot mechanically migrate within Task 6.
- Task 4 preconditions (shared cookie contract test fixture) are not met by the time Task 4 is scheduled.
- Task 5 decision (implement vs. remove) is unresolved.
- The `release-gate` job flags a new capability-registry inconsistency that this plan did not anticipate.

## Final Review Checklist

- Public API inventory diff matches the sum of Tasks 1 through 5 and nothing else.
- `framework-capabilities.json` is consistent with the migrated public surface — no dangling capability id, no missing one.
- `docs/MIGRATION_GUIDE.md` covers every single removal or rename with a mechanical rule.
- `CHANGELOG.md` `## [2.0.0]` section is complete.
- CI `release-gate` is green on `release/v2` at the tag creation commit.
