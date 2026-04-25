# Fulora v2.0 Apple Shim Modernization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Planning. No code changes until this plan is accepted and the `release/v2` branch carving is coordinated with `2026-04-23-fulora-v2-public-api-breakage.md`.

**Goal:** Replace the entire Objective-C `.mm` shim layer (`src/Agibuild.Fulora.Platforms/MacOS/Native/` + `src/Agibuild.Fulora.Adapters.iOS/Native/`, ~2953 lines of ObjC + xcframework / `.a` / `.o` artifacts) with a pure C# runtime delegate implementation that constructs Objective-C classes via the ObjC runtime API (`AllocateClassPair` + `class_addMethod`), eliminating Xcode / xcodebuild from the Apple build pipeline and bringing the Apple platform line to architectural parity with Avalonia.Controls.WebView's `Macios/` organization.

**Architecture:** Vendor the MIT-licensed Avalonia.Controls.WebView `Macios/Interop/` foundation (ObjC runtime bindings, NSObject managed-self pattern, BlockLiteral, basic Foundation types) into `src/Agibuild.Fulora.Platforms/Macios/Interop/` with attribution. Build Fulora-specific upper layers (WKURLSchemeHandler, WKDownloadDelegate, WKWebsiteDataStore cookie operations, WKUIDelegate media permission, Security.framework SecTrust/SecCertificate) that Avalonia does not implement. Keep `src/Agibuild.Fulora.Platforms` (multi-TFM, hosts macOS slice on `net10.0-macos` once added) and `src/Agibuild.Fulora.Adapters.iOS` (separate project for `net10.0-ios` workload constraints) as separate csproj boundaries during this plan; their *adapters* both consume the same `Macios/` namespace, eliminating the per-platform native shim duplication. Apple SSL policy hooks land at parity P-2 (full server certificate metadata) inside the same plan because we now hold `SecTrust` in managed code.

**Tech Stack:** .NET 10 (`net10.0`, `net10.0-ios`, `net10.0-macos`), C# 12, ObjC runtime API via `/usr/lib/libobjc.dylib`, Apple Foundation / WebKit / Security frameworks via `objc_msgSend` and `DllImport`, xUnit, Verify, Stryker.NET, Nuke build, GitHub Actions CI.

---

## Cross-Plan Coordination

This plan depends on or coordinates with the following:

| Plan | Relationship | Concrete coupling |
|---|---|---|
| `docs/superpowers/plans/2026-04-23-navigation-ssl-policy-explicit.md` | **Predecessor with explicit branch decision** | The contracts (`INavigationSecurityHooks` / `ServerCertificateErrorContext` / `WebViewSslException` extended ctor) MUST be merged before this plan starts (Phase 1 depends on them as references). The Apple-platform Tasks (b5 = Apple adapters, b8 = mock + contract, b9 = verify, b10 = version) of the predecessor plan are **subject to the branch decision below** — they may complete on `1.x` ObjC code, OR they may be cancelled in favor of landing P-1 + P-2 together in this plan's Phase 4. See "Predecessor Branch Decision" below. |
| `docs/superpowers/plans/2026-04-23-fulora-v2-public-api-breakage.md` | **Parallel sibling** | Both target `release/v2`. No shared *source* files. API breakage owns `Agibuild.Fulora.Core` public surface; this plan owns `Agibuild.Fulora.Platforms` (Apple slices) + `Agibuild.Fulora.Adapters.iOS`. **Both plans touch `CHANGELOG.md`, `docs/MIGRATION_GUIDE.md`, `docs/framework-capabilities.json`, `Directory.Build.props` (VersionPrefix)** — see "Sibling Plan Merge Order" below for the coordination rule. |
| `docs/framework-capabilities.json` | **Capability registry update (shared with sibling)** | Add a `platforms.apple.shim.modernized` capability row in **Phase 7** (Task 31) with `status: stable` only after Phase 7 verification passes. Sibling v2 API breakage plan also touches this file — see "Sibling Plan Merge Order" below to avoid conflicts. |

### Predecessor Branch Decision (resolve before Task 1)

The predecessor `navigation-ssl-policy-explicit` plan describes Apple P-1 implementation by modifying the existing `.mm` shim (`WkWebViewShim.mm` / `WkWebViewShim.iOS.mm`). If those changes ship on `1.x`, this plan would have to **revert** them when cutting Apple to managed code, then re-implement P-2.

**Two branches; pick one before opening this plan's first PR:**

| Branch | Pros | Cons |
|---|---|---|
| **(A) Ship P-1 in ObjC on 1.x first** | Apple users get a critical SSL fix sooner; predecessor plan completes cleanly. | Wasted ObjC work in P-1 that gets deleted in this plan's Phase 6; risks ObjC-side regression if predecessor takes long. |
| **(B) Skip Apple ObjC P-1; land P-1 + P-2 together in this plan's Phase 4** | No throwaway ObjC; one cohesive Apple security upgrade. | Apple SSL fix delayed until `2.0.0-preview.5`; predecessor plan exits with Apple Tasks `b5/b8` marked **cancelled (superseded by v2 apple-shim plan)**. |

**Recommendation:** **Branch B**. The throwaway ObjC work in (A) is non-trivial (override `webView:didReceiveAuthenticationChallenge:` in `.mm` requires native build pipeline anyway, which is exactly what we're killing). Branch B aligns the SSL upgrade with the modernization without compromising the security posture beyond a `~2 preview` delay.

Document the chosen branch with a one-line update in the predecessor plan's task list (`b5/b8/b9/b10` → `cancelled` if Branch B) before this plan's Task 1.

### Sibling Plan Merge Order

Both v2 plans land into `release/v2` and both touch shared metadata files. Enforce these rules **manually** (no CI gate — the rule is for the human merging PRs):

1. **Each preview tag belongs to exactly one plan.** `2.0.0-preview.{1,2}` belongs to API breakage (per its own cadence table). `2.0.0-preview.{3..7}` belongs to this plan. Sibling tags must not be skipped or duplicated.
2. **Shared-file edits go in the latter PR of any given preview window.** If a preview window has open work in both plans, merge the API-breakage PR first (touches Core only), then this plan's PR (touches Platforms + same shared metadata files); the shared-file edits in this plan's PR rebase cleanly because Core changes don't conflict with Platforms.
3. **`Directory.Build.props` `VersionPrefix` is bumped exactly once per preview tag** by whichever plan is sealing that preview. The `nuke UpdateVersion` tool enforces single-source-of-truth.
4. **No simultaneous force-push** to `release/v2` from either plan. Use rebase + standard PR merges.

## Scope Guardrails

- **Do** vendor Avalonia interop files **as-is** with namespace rename + attribution comment; do **not** rewrite for "style" — diff drift breaks future upstream patch absorption.
- **Do** keep `src/Agibuild.Fulora.Platforms/Macios/` as a single namespace shared by the macOS slice (**`net10.0` + `[SupportedOSPlatform("macos")]` runtime gate** — see Spike 0a result; no `net10.0-macos` TFM is added) and consumed by `src/Agibuild.Fulora.Adapters.iOS` (`net10.0-ios` TFM) via `<Compile Include="..\Agibuild.Fulora.Platforms\Macios\**\*.cs" />` link items, so the Apple managed surface is single-source.
- **Do not** merge `Agibuild.Fulora.Adapters.iOS` into `Agibuild.Fulora.Platforms` in this plan; iOS workload requires its own SDK targets that fight Multi-TFM in the same csproj. That consolidation is a follow-up plan after this lands stable.
- **Do not** change adapter public surface (`IWebView`, `IWebViewBridge`, `ICookieAdapter`, etc.). All changes are internal implementation. Public-API breakage is owned by the sibling v2 plan.
- **Do not** cut over macOS and iOS in the same commit. Each adapter cuts over independently with its own integration regression. macOS first (lower-risk, full host control), iOS second.
- **Do not** ship behind a feature flag. There is no "old shim path" + "new C# path" coexistence — that doubles the maintenance surface and obscures regressions. Cut over per adapter, fail forward.
- **Do not** keep any `.mm` / `.m` / `.h` / `.a` / `.o` / `.xcframework` after Phase 6. The cleanup is part of the contract.

## Architectural Invariants

- **Every Apple platform path goes through the same `Macios/` namespace.** No per-platform conditional `#if IOS` / `#if MACOS` branching of the interop layer; only the *adapter* differentiates host setup (NSWindow vs UIView).
- **The ObjC runtime managed-self pattern is the single source of identity for delegates.** No GCHandle leaks: every `RegisterManagedMembers`-allocated handle is freed in the corresponding `dealloc` selector implementation.
- **Every WebKit delegate method lives in exactly one file.** No copy-paste between `WKNavigationDelegate.cs` and `WKUIDelegate.cs`; shared helpers go to `WkDelegateBase.cs`.
- **Security.framework P/Invokes are wrapped in idiomatic `IDisposable` types** that release `CFTypeRef` correctly — never raw `IntPtr` leaking out of the `Macios.Interop.Security` namespace.
- **AOT/Trim safe by construction:** `AllocateClassPair` is dynamic but `class_addMethod` arguments are all `delegate*` and `static readonly IntPtr` — no reflection. AOT publish (`net10.0-ios`) must succeed in Phase 7 with `IsTrimmable=true` and `PublishAot=true`.
- **The `INavigationSecurityHooks` contract from the predecessor plan is unchanged.** Apple's upgrade is filling more fields of `ServerCertificateErrorContext`, not changing the hook signature.

## Rejected Alternatives

| Alternative | Rejection reason |
|---|---|
| Take a NuGet dependency on `Avalonia.Controls.WebView` and reuse its `Macios.Interop` directly | Brings Avalonia framework dependency transitively; types are `internal`; `MaciosWebViewAdapter` couples to Avalonia composition. Vendoring with attribution is the only clean path. |
| Keep `.mm` shim and just port one delegate (the SSL one) to managed | Architectural inconsistency: half the Apple integration speaks ObjC, half speaks C#. Doubles future cognitive load and CI complexity. Either fully migrate or do not start. |
| Rewrite shim in Swift instead of C# runtime delegate | Swift adds another toolchain (`swiftc`) and a stable ABI question for static linking. Net change: replace one external toolchain (`xcodebuild`) with another (`swiftc`). Provides no benefit over status quo. |
| Generate ObjC delegate stubs from .NET source generator at build time | Source generators cannot emit ObjC; would require generating C ABI shims that still need a native compile step. Solves nothing. |
| Keep iOS shim as `.mm` because iOS device builds require Xcode anyway | iOS `net10.0-ios` builds only require Xcode for *AOT linking* (which `dotnet publish` orchestrates), not for compiling a separate `.a`. Keeping the shim drags an additional `xcodebuild` step that's independent of the workload. |

## Expected Version Cadence

| Tag | Scope completed |
|---|---|
| `2.0.0-preview.3` | Phase 1 (Macios interop vendored + namespace) merged. **No feature flag** — the new code is simply not yet referenced by any adapter; the existing `.mm` shim still ships. |
| `2.0.0-preview.4` | Phase 2 + Phase 3 (managed WKWebView + delegates registered, but adapters still on `.mm`). Internal smoke tests prove the new path is functional. |
| `2.0.0-preview.5` | Phase 4 (Security framework + SSL P-2 internally validated, still not wired to adapters). |
| `2.0.0-preview.6` | Phase 5 macOS cutover. macOS adapter on pure C#; iOS still on shim. |
| `2.0.0-preview.7` | Phase 5 iOS cutover. Both adapters on pure C#; `.mm` shim still present but unreferenced. |
| `2.0.0-rc.1` | Phase 6 cleanup + Phase 7 verification. `.mm` / `.a` / `.xcframework` deleted. CI pipelines no longer touch Xcode for native compile. |
| `2.0.0` | After at least one full release cycle on `2.0.0-rc.1` with no Apple regression. |

---

## File Structure (informs task decomposition)

```
src/Agibuild.Fulora.Platforms/
├─ Macios/                                    ← NEW (this plan)
│  ├─ Interop/
│  │  ├─ Libobjc.cs                          T2  vendor + namespace
│  │  ├─ NSObject.cs                          T2  vendor + namespace
│  │  ├─ NSManagedObjectBase.cs               T2  vendor + namespace
│  │  ├─ BlockLiteral.cs                      T3  vendor + namespace
│  │  ├─ Foundation/
│  │  │  ├─ NSError.cs                        T4  vendor
│  │  │  ├─ NSURL.cs                          T4  vendor
│  │  │  ├─ NSURLRequest.cs                   T4  vendor
│  │  │  ├─ NSMutableURLRequest.cs            T4  vendor
│  │  │  ├─ NSString.cs                       T4  vendor
│  │  │  ├─ NSData.cs                         T4  new (Avalonia missing)
│  │  │  ├─ NSDictionary.cs                   T4  vendor
│  │  │  ├─ NSArray.cs                        T4  vendor
│  │  │  ├─ NSDate.cs                         T4  vendor
│  │  │  ├─ NSNumber.cs                       T4  vendor
│  │  │  ├─ NSUUID.cs                         T4  vendor
│  │  │  ├─ NSHTTPCookie.cs                   T9  vendor
│  │  │  └─ NSHTTPCookieStore.cs              T9  new
│  │  ├─ WebKit/
│  │  │  ├─ WKWebKit.cs                       T6  protocol resolution helpers (vendor)
│  │  │  ├─ WKWebView.cs                      T6  vendor + extend
│  │  │  ├─ WKWebViewConfiguration.cs         T7  new
│  │  │  ├─ WKPreferences.cs                  T7  new
│  │  │  ├─ WKWebpagePreferences.cs           T7  new
│  │  │  ├─ WKUserContentController.cs        T8  new
│  │  │  ├─ WKWebsiteDataStore.cs             T9  new
│  │  │  ├─ WKURLSchemeHandler.cs             T10 new
│  │  │  ├─ WKURLSchemeTask.cs                T10 new
│  │  │  ├─ WkDelegateBase.cs                 T11 new (shared helpers)
│  │  │  ├─ WKNavigationDelegate.cs           T11 vendor + extend (5 existing methods)
│  │  │  ├─ WKUIDelegate.cs                   T12 new
│  │  │  ├─ WKScriptMessageHandler.cs         T13 new
│  │  │  ├─ WKURLSchemeHandlerImpl.cs         T14 new
│  │  │  └─ WKDownloadDelegate.cs             T15 new
│  │  └─ Security/
│  │     ├─ Security.cs                       T16 new (P/Invoke surface)
│  │     ├─ SecTrust.cs                       T16 new
│  │     ├─ SecCertificate.cs                 T16 new
│  │     └─ X509MetadataExtractor.cs          T16 new
│  ├─ MacOS/
│  │  └─ MacOSWebViewAdapter.cs               T19 cutover (existing file, refactor body)
│  └─ ATTRIBUTION.md                          T1  Avalonia MIT attribution + per-file mapping
├─ Apple/                                     (current MacOS folder; renamed in T1)
│  └─ ...
└─ Native/                                    DELETE in T22

src/Agibuild.Fulora.Adapters.iOS/
├─ iOSWebViewAdapter.cs                       T20 cutover (existing file, refactor body)
├─ Agibuild.Fulora.Adapters.iOS.csproj        T20 add <Compile Include="..\Macios\**\*.cs" Link="..."/>
└─ Native/                                    DELETE in T23

build/Build.cs                                T24 remove BuildAppleNative target
.github/workflows/ci.yml                      T25 remove Apple shim build step
.gitignore                                    T26 remove *.xcframework / *.a / *.o entries

tests/Agibuild.Fulora.Platforms.UnitTests/    NEW (this plan)
└─ Macios/
   ├─ Interop/
   │  ├─ LibobjcSmokeTests.cs                T5
   │  ├─ NSObjectLifecycleTests.cs            T5
   │  └─ NSStringRoundtripTests.cs            T5
   │     (block invocation covered by T9 + Phase 0b — no synthetic test)
   ├─ WebKit/
   │  ├─ WKWebViewSmokeTests.cs              T6,T7
   │  ├─ WKNavigationDelegateTests.cs        T11
   │  ├─ WKUIDelegateTests.cs                T12
   │  └─ WKScriptMessageHandlerTests.cs      T13
   └─ Security/
      ├─ X509MetadataExtractorTests.cs        T16
      └─ ServerTrustChainTests.cs             T17

tests/Agibuild.Fulora.UnitTests/
└─ Security/
   └─ AdapterSslRejectionContractAppleSliceTests.cs  T18 (cross-plat contract Apple slice)
```

---

## Phase 0 — De-risking Spikes (resolve before Phase 1)

These three spikes resolve the design decisions that, if punted, could invalidate the entire plan after weeks of work. Each spike has a hard go/no-go gate.

### Spike 0a — `net10.0-macos` TFM decision

**Question:** Does the Apple slice need a dedicated `net10.0-macos` TFM, or is `net10.0` + `[SupportedOSPlatform("macos")]` runtime gating sufficient?

**Method:** Audit the existing `MacOSWebViewAdapter.cs` for any API requiring a macOS-specific TFM (e.g. `Foundation.NSObject` from `Microsoft.macOS.SDK`). Build a 50-line spike adapter under `net10.0` that calls `dlopen("/usr/lib/libobjc.dylib")` and `objc_getClass("NSObject")` — if it links and runs on macOS host, `net10.0` is sufficient.

**Gate:**
- **`net10.0` sufficient** → keep current TFM, remove "add `net10.0-macos`" steps from Task 19.
- **`net10.0-macos` required** → keep Task 19 Step 1 as written; document the API that forced the decision in `docs/platform-status.md`.

**Output:** A one-paragraph decision in `docs/superpowers/plans/2026-04-25-spike-results.md` linked from this plan; update Open Question 1.

**Time budget:** 0.5 day. **Hard stop:** if more than 1 day, escalate.

**RESOLVED 2026-04-25 — PASS.** `net10.0` + `[SupportedOSPlatform("macos")]` runtime gate is sufficient. All 6 probes (`dlopen libobjc`, `objc_getClass(NSObject)`, `sel_registerName`, `dlopen WebKit.framework`, `objc_getClass(WKWebView)`, `dlopen Security.framework`) succeeded under `net10.0` on macOS 26.4.1 / .NET 10.0.5 / Arm64. **Task 19 Step 1 (`net10.0-macos` TFM addition) is REMOVED below.** See [`2026-04-25-spike-results.md`](./2026-04-25-spike-results.md) § Spike 0a for full evidence.

---

### Spike 0b — `BlockLiteral` ABI on iOS arm64

**Question:** Does Avalonia's `BlockLiteral` (designed for macOS arm64 + x64) work correctly on iOS arm64 (real device + simulator), specifically for the block-layout descriptor?

**Method:**
1. Vendor `BlockLiteral.cs` (Task 3 preview).
2. Build a tiny iOS test app (`net10.0-ios` simulator) that constructs a block via `BlockLiteral`, hands it to a known WebKit async API (e.g. `WKHTTPCookieStore.GetAllCookies(completionHandler:)`), and asserts the completion fires with the expected payload.
3. Test on **both** iOS Simulator (x64 or arm64 depending on host) and a real iOS arm64 device if available.

**Gate:**
- **Block dispatches correctly on all targets** → proceed; document in spike results.
- **iOS arm64 layout differs** → Phase 1 must extend `BlockLiteral.cs` with an `iOS arm64` block descriptor variant before Task 3 finalises. Estimate 2-3 extra days; if the variant is non-trivial, escalate to human for plan revision.

**Time budget:** 2 days. **Hard stop:** if not resolved in 3 days, escalate.

---

### Spike 0c — AOT publish smoke for `AllocateClassPair` + `class_addMethod` from C#

**Question:** Does the runtime ObjC class registration approach (`Libobjc.AllocateClassPair` + `class_addMethod` with `delegate*<>` and `[UnmanagedCallersOnly]`) survive AOT publish on `net10.0-ios`?

**Method:**
1. Take the spike adapter from Spike 0a + the `BlockLiteral` from Spike 0b.
2. Add a single `WKNavigationDelegate`-shaped runtime class with one registered selector (`webView:didFinishNavigation:`).
3. Run `dotnet publish -c Release -f net10.0-ios -p:PublishAot=true` in a minimal iOS app.
4. Run the published app on iOS Simulator; assert the registered selector fires when the WKWebView completes a navigation.

**Gate:**
- **AOT publish succeeds + selector dispatches** → proceed; AOT is viable.
- **AOT publish fails with IL3xxx/IL2xxx that cannot be annotated away** → the entire C# delegate registration approach is wrong for AOT iOS. Escalate to human; the plan must pivot to a hybrid (managed for non-AOT, ObjC for AOT) or stay on `.mm`.

**Time budget:** 2 days. **Hard stop:** if not resolved in 3 days, escalate. **This is the single largest "kill switch" for the whole plan.**

---

### Spike 0 Exit Criteria

All three spikes pass → tag spike results commit + update Open Questions in this plan with concrete answers → proceed to Phase 1.

Any spike fails → **stop**, do not start Phase 1, escalate to human with spike results + suggested plan revision.

> **STATUS 2026-04-25 — SATISFIED. Phase 1 cleared to begin.** All three spikes PASS:
> - Spike 0a: `net10.0` sufficient (commit `bcf728d`)
> - Spike 0b: `BlockLiteral` works on iOS Simulator arm64 (commit `9e2d0aa`)
> - Spike 0c: AOT viable for `AllocateClassPair` + `class_addMethod` (commit `d9d5838`, with simulator-RID caveat captured at Task 30)
>
> Aggregate result + plan amendments landed in [`2026-04-25-spike-results.md`](./2026-04-25-spike-results.md) § Phase 0 GO/NO-GO Aggregate.

---

## Phase 1 — Macios Interop Foundation (vendor + adapt)

> **Vendoring policy (applies to ALL Phase 1, 2, 3 vendor tasks — added 2026-04-25 after T2 dry-run):**
>
> The repo enforces `EnforceCodeStyleInBuild=true` + `TreatWarningsAsErrors=true` (set in `Directory.Build.props`). Upstream Avalonia.Controls.WebView is style-permissive (e.g., omits explicit `private` on members where it is the implicit default), which collides with our enforced style rules. Fixing this at the repo level (suppressing IDE0040 for `Macios/**`) would violate the project's "禁止不解决root cause 而掩盖警告" rule and ship code that's stylistically worse than the rest of the repo.
>
> **Authorized vendor modifications (in addition to SPDX header + namespace rename):**
>
> 1. **Explicit access modifiers** to satisfy IDE0040 (`Accessibility modifiers required`). The implementer MAY add `private` (or whichever modifier is the C# implicit default for that declaration site) to fields, methods, properties, nested types, and operators. The added modifier MUST match what C# would default to implicitly — i.e., this is a no-behavior-change normalization, not a visibility change.
> 2. **No other style fixes.** Do NOT reorder usings, do NOT change formatting, do NOT rename identifiers, do NOT remove unused usings, do NOT split or merge files. Each style normalization must be the minimum required to satisfy a specific build-error analyser rule.
> 3. **Discovery procedure:** Each implementer MUST first run the build smoke and let the compiler enumerate the exact lines that fail. Patch only those lines. No speculative or repo-wide changes.
> 4. **Tracking:** No per-file modification log is required (the SPDX header already declares "Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md"). The vendoring SHA in ATTRIBUTION.md is sufficient — anyone re-vendoring at a future SHA will re-run the build smoke and re-apply identical normalizations.
> 5. **Unauthorized fallbacks (forbidden):** Do NOT add `<NoWarn>IDE0040</NoWarn>`. Do NOT edit `.editorconfig` to relax `dotnet_diagnostic.IDE0040.severity` for `Macios/**`. Do NOT disable `EnforceCodeStyleInBuild` or `TreatWarningsAsErrors` for the project.
>
> If a different analyser rule (other than IDE0040) blocks build smoke during a vendor task, the implementer MUST report `BLOCKED` with the exact rule ID and lines, NOT silently apply a fix outside the authorized list above. The plan owner will then either expand this authorization list or reject the rule.

### Task 1: Create `Macios/` directory tree and Avalonia attribution

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/` (folder only)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/` (folder)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/` (folder)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Security/` (folder)
- Create: `src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md`

- [ ] **Step 1: Create folder skeleton**

```bash
mkdir -p src/Agibuild.Fulora.Platforms/Macios/{Interop/Foundation,Interop/WebKit,Interop/Security}
```

- [ ] **Step 2: Write attribution document**

Create `src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md`:

```markdown
# Macios Interop — Attribution

This namespace contains code originally derived from the
[Avalonia.Controls.WebView](https://github.com/AvaloniaUI/Avalonia.Controls.WebView)
project, MIT-licensed by AvaloniaUI OÜ. The vendored files are listed below
with their upstream paths so future upstream patches can be re-applied.

| Local file | Upstream file | Vendored at commit |
|---|---|---|
| `Interop/Libobjc.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/Libobjc.cs` | TBD (filled in Task 2) |
| `Interop/NSObject.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSObject.cs` | TBD |
| `Interop/NSManagedObjectBase.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSManagedObjectBase.cs` | TBD |
| `Interop/BlockLiteral.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/BlockLiteral.cs` | TBD |
| `Interop/Foundation/*` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/{NS*}.cs` | TBD |
| `Interop/WebKit/WKWebView.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/WebKit/WKWebView.cs` | TBD |
| `Interop/WebKit/WKNavigationDelegate.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/WebKit/WKNavigationDelegate.cs` | TBD |

## License

Original copyright: (c) 2026 AvaloniaUI OÜ — MIT License
Modifications: (c) 2026 Agibuild — MIT License (see repo root LICENSE).

Each vendored file carries a per-file SPDX header indicating both copyrights.
```

- [ ] **Step 3: Verify**

```bash
test -d src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation \
  && test -d src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit \
  && test -d src/Agibuild.Fulora.Platforms/Macios/Interop/Security \
  && test -f src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md \
  && echo OK
```
Expected: `OK`

- [ ] **Step 4: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/
git commit -m "chore(apple): scaffold Macios/ namespace with Avalonia MIT attribution"
```

---

> **AMENDMENT (2026-04-25, applied during Phase 1 execution):** The original T2/T3 split (T2 = Libobjc + NSObject + NSManagedObjectBase; T3 = BlockLiteral) is **infeasible** because `Libobjc.cs` declares
> `[LibraryImport(libobjc)] public static partial IntPtr _Block_copy(BlockLiteral* block);` (line 15 in upstream `4e16564`), which references `BlockLiteral`. T2's Step 5 build smoke would fail with `CS0246: BlockLiteral`. Suppressing or commenting the `_Block_copy` line during T2 would be a defensive temporary-broken-state hack that violates the project's "no defensive compatibility hacks" rule.
>
> **Re-grouping (preserves task numbers, swaps file ownership):**
> - **T2 (low-level interop bedrock)** = `Libobjc.cs` + `BlockLiteral.cs` — the coupled pair that bottoms out the unmanaged surface.
> - **T3 (managed object base classes)** = `NSObject.cs` + `NSManagedObjectBase.cs` — depend on Libobjc but not on BlockLiteral; can compile after T2 lands.
>
> Both tasks remain independent build-clean checkpoints. ATTRIBUTION.md row order is unchanged (Libobjc, NSObject, NSManagedObjectBase, BlockLiteral) for upstream-path stability — T2 fills 2 SHA rows (Libobjc + BlockLiteral, the latter despite being row 4), T3 fills the other 2 (NSObject, NSManagedObjectBase).

### Task 2: Vendor `Libobjc.cs` + `BlockLiteral.cs` + `CGRect.cs` (low-level interop bedrock)

> **AMENDMENT #2 (2026-04-25, applied during T2 dry-run):** `Libobjc.cs` declares msgSend overloads with `CGRect` / `CGSize` parameter and return types (lines 81, 109, 115, 117 in upstream `4e16564`). Upstream defines both record structs in a single 179-byte `Macios/Interop/CGRect.cs`. Without vendoring `CGRect.cs`, T2 build smoke fails with `CS0246: CGRect / CGSize`. CGRect.cs is self-contained (no transitive deps, no `using` directives) so adding it to T2 is a clean expansion. ATTRIBUTION.md gains one new row for `Interop/CGRect.cs`.

**Files:**
- Copy from: `https://github.com/AvaloniaUI/Avalonia.Controls.WebView` `main` branch (record commit SHA)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Libobjc.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/BlockLiteral.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/CGRect.cs` (contains `CGRect` + `CGSize` record structs — single upstream file)

- [ ] **Step 1: Clone Avalonia.Controls.WebView at pinned SHA**

```bash
cd /tmp
rm -rf avalonia-webview
git clone --depth=1 https://github.com/AvaloniaUI/Avalonia.Controls.WebView.git avalonia-webview
cd avalonia-webview
git rev-parse HEAD > /tmp/avalonia-vendor-sha.txt
cat /tmp/avalonia-vendor-sha.txt
```

- [ ] **Step 2: Copy three files into `Macios/Interop/`**

```bash
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/Libobjc.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/Libobjc.cs
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/BlockLiteral.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/BlockLiteral.cs
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/CGRect.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/CGRect.cs
```

- [ ] **Step 3: Rename namespace + add SPDX header in each file**

For each of the three files, replace:

```csharp
namespace Avalonia.Controls.Macios.Interop;
```

with:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

namespace Agibuild.Fulora.Platforms.Macios.Interop;
```

Update any internal `using` of `Avalonia.Controls.Macios.*` to `Agibuild.Fulora.Platforms.Macios.*`.

- [ ] **Step 4: Update ATTRIBUTION.md with vendor SHA**

In `Macios/ATTRIBUTION.md`:

1. Replace `TBD (filled in Task 2)` (Libobjc row) with the bare SHA from `/tmp/avalonia-vendor-sha.txt`.
2. Replace `TBD` in the BlockLiteral row (4th row) with the same SHA.
3. **Insert a new row** for `CGRect.cs` immediately AFTER the BlockLiteral row, with content:
   ```
   | `Interop/CGRect.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/CGRect.cs` | <SHA> |
   ```
   (where `<SHA>` is the same vendor SHA). The CGRect row is the only row with non-TBD content for a "vendored as part of T2" file beyond the original 7-row table.
4. Leave `NSObject.cs`, `NSManagedObjectBase.cs`, and Foundation/WebKit rows as `TBD` for later tasks.

- [ ] **Step 5: Build smoke**

```bash
dotnet build src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj -c Release --nologo
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. (Files compile but are not yet referenced by adapters.)

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/{Libobjc,BlockLiteral,CGRect}.cs \
        src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md
git commit -m "chore(apple): vendor Avalonia Libobjc + BlockLiteral + CGRect interop bedrock"
```

---

> **AMENDMENT #3 (2026-04-25, applied during T3 dry-run):** `NSObject.cs` line 75 calls `NSString.GetString(...)` directly (in `GetDescription`). The original T3/T4 split (NSObject in T3, NSString authored fresh in T4) would fail T3 build smoke with `CS0246: NSString`. The original T4 stub also incorrectly stated "Avalonia inlines string conv into NSObject" — `NSString.cs` actually exists upstream as a separate 67-line file with the same `Avalonia.Controls.Macios.Interop` namespace. NSString.cs depends only on NSObject (parent) + Libobjc (already in T2), so no further transitive dependencies. Re-grouping:
>
> - **T3 (managed object base + string identity)** = `NSObject.cs` + `NSManagedObjectBase.cs` + `NSString.cs` — vendored together at the `Macios/Interop/` root (mirrors upstream's flat layout; NSString is *not* moved into `Foundation/` because NSObject directly references it, which would create an awkward upward namespace `using` from the runtime layer to the Foundation framework layer).
> - **T4 (Foundation framework types)** loses the "author NSString.cs" step; the `Foundation/` subdirectory contents are unchanged otherwise.
>
> ATTRIBUTION.md gains one new row for `Interop/NSString.cs`, inserted **immediately AFTER `NSManagedObjectBase.cs`** so the runtime-layer rows (Libobjc → NSObject → NSManagedObjectBase → NSString → BlockLiteral → CGRect) cluster together at the top of the table; Foundation/* row remains last.

### Task 3: Vendor `NSObject.cs` + `NSManagedObjectBase.cs` + `NSString.cs` (managed object base classes)

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/NSObject.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/NSManagedObjectBase.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/NSString.cs`

- [ ] **Step 1: Copy three files from the T2-pinned upstream clone**

```bash
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/NSObject.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/NSObject.cs
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/NSManagedObjectBase.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/NSManagedObjectBase.cs
cp /tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop/NSString.cs \
   src/Agibuild.Fulora.Platforms/Macios/Interop/NSString.cs
```

- [ ] **Step 2: Apply same SPDX header + namespace rename pattern as Task 2 Step 3** to all three files. (Per the Phase 1 Vendoring Policy at the top of this phase, also add explicit `private`/etc. modifiers if and only if the build smoke surfaces IDE0040 errors at specific lines.)

- [ ] **Step 3: Update ATTRIBUTION.md**
  1. Replace `TBD` in the `Interop/NSObject.cs` row with `cat /tmp/avalonia-vendor-sha.txt`.
  2. Replace `TBD` in the `Interop/NSManagedObjectBase.cs` row with the same SHA.
  3. **Insert a new row immediately AFTER the `Interop/NSManagedObjectBase.cs` row** with content:
     ```
     | `Interop/NSString.cs` | `src/Avalonia.Controls.WebView.Core/Macios/Interop/NSString.cs` | <SHA> |
     ```

- [ ] **Step 4: Build smoke**

```bash
dotnet build src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj -c Release --nologo
```
Expected: 0 errors / 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/{NSObject,NSManagedObjectBase,NSString}.cs \
        src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md
git commit -m "chore(apple): vendor Avalonia NSObject + NSManagedObjectBase + NSString managed wrappers"
```

---

### Task 4: Vendor Foundation types (NSError, NSURL, NSURLRequest, NSMutableURLRequest, NSDictionary, NSArray, NSDate, NSNumber, NSUUID) + new NSData

> **AMENDMENT #4 (2026-04-25, applied via T3 amendment #3):** `NSString.cs` is no longer authored fresh in T4 — it is vendored from upstream as part of T3 (because `NSObject.cs` directly references it). T4 now vendors 9 Foundation types + authors only 1 new file (`NSData.cs`).

> **On bite-sized task granularity:** This task vendors 9 files + authors 1 in one commit, which is larger than the 2-5 minute step rule suggests. The justification: each vendored file is a near-mechanical paste + namespace rename automated by the Step 2 script. There is no per-file design decision and no per-file rollback value (you would never want to ship `NSURL.cs` without `NSError.cs`). If the implementer prefers finer commits for bisect granularity, split into **T4a: 9 vendored Foundation types** and **T4b: NSData (newly authored)** — both options are acceptable.

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSError.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSURL.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSURLRequest.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSMutableURLRequest.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSDictionary.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSArray.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSDate.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSNumber.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSUUID.cs` (vendor)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSData.cs` (NEW — Avalonia does not have this)

- [ ] **Step 1: Bulk copy vendor files**

```bash
SRC=/tmp/avalonia-webview/src/Avalonia.Controls.WebView.Core/Macios/Interop
DST=src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation
for f in NSError.cs NSURL.cs NSURLRequest.cs NSMutableURLRequest.cs \
         NSDictionary.cs NSArray.cs NSDate.cs NSNumber.cs NSUUID.cs; do
  cp "$SRC/$f" "$DST/$f"
done
```

- [ ] **Step 2: Apply SPDX header + namespace rename to each file** (use a script to avoid drift):

```bash
for f in src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NS*.cs; do
  python3 - "$f" <<'PY'
import sys
p = sys.argv[1]
src = open(p).read()
header = ("// SPDX-License-Identifier: MIT\n"
          "// Copyright (c) 2026 AvaloniaUI OÜ\n"
          "// Copyright (c) 2026 Agibuild\n"
          "// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.\n\n")
src = src.replace("namespace Avalonia.Controls.Macios.Interop;",
                  "namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;")
src = src.replace("using Avalonia.Controls.Macios.Interop;",
                  "using Agibuild.Fulora.Platforms.Macios.Interop;")
open(p, "w").write(header + src)
PY
done
```

- [ ] **Step 3: Author NEW `NSData.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal sealed class NSData : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSData");
    private static readonly IntPtr s_dataWithBytes = Libobjc.sel_getUid("dataWithBytes:length:");
    private static readonly IntPtr s_bytes = Libobjc.sel_getUid("bytes");
    private static readonly IntPtr s_length = Libobjc.sel_getUid("length");

    private NSData(IntPtr handle) : base(handle) { }

    public static NSData FromBytes(ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                var handle = Libobjc.intptr_objc_msgSend_intptr_nuint(
                    s_class, s_dataWithBytes, (IntPtr)p, (nuint)data.Length);
                return new NSData(handle);
            }
        }
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        var ptr = Libobjc.intptr_objc_msgSend(Handle, s_bytes);
        var len = (long)Libobjc.nuint_objc_msgSend(Handle, s_length);
        unsafe { return new ReadOnlySpan<byte>((void*)ptr, checked((int)len)); }
    }

    public byte[] ToArray() => AsSpan().ToArray();
}
```

- [ ] **Step 4: Build smoke**

```bash
dotnet build src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj -c Release --nologo
```
Expected: 0 errors / 0 warnings. If `Libobjc` is missing one of the `intptr_objc_msgSend_*` overloads invoked in NSData, add the overload to `Libobjc.cs` in this commit (Avalonia ships a wide overload table; usually all needed are present).

- [ ] **Step 5: Update ATTRIBUTION.md** with the 9 vendored Foundation types (replace the single `Interop/Foundation/*` row with explicit per-file rows, each carrying the same vendor SHA from `cat /tmp/avalonia-vendor-sha.txt`).

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/ \
        src/Agibuild.Fulora.Platforms/Macios/Interop/Libobjc.cs \
        src/Agibuild.Fulora.Platforms/Macios/ATTRIBUTION.md
git commit -m "chore(apple): vendor Avalonia Foundation types + add NSData"
```

---

### Task 5: Foundation interop sanity tests (host: macOS)

> **Why a new `Agibuild.Fulora.Platforms.UnitTests` project (vs reusing `Agibuild.Fulora.UnitTests`):** Existing `Agibuild.Fulora.UnitTests` runs cross-platform on every CI matrix slot. Loading `Macios/Interop/*` into that project would (a) drag macOS-only dependencies into Linux/Windows test runs, (b) need `if (OperatingSystem.IsMacOS()) return;` early-return on every test (illusory coverage on non-macOS hosts — see Step 6 warning), and (c) inflate restore times for everyone. A dedicated project that runs **only** on the macOS CI job is the cleanest separation. Document this rationale in the new csproj's header comment.

**Files:**
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/` (new test project)
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj`
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Interop/LibobjcSmokeTests.cs`
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Interop/NSObjectLifecycleTests.cs`
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Interop/NSStringRoundtripTests.cs`

(No `BlockLiteralInvokeTests.cs` here — block invocation is exercised end-to-end by T9 `WKHTTPCookieStoreTests` and validated by Phase 0b spike. A synthetic block test would only re-prove what those cover.)

- [ ] **Step 1: Create test csproj (macOS host gated)**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Agibuild.Fulora.Platforms\Agibuild.Fulora.Platforms.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution + Directory.Packages.props (if package versions missing).**

```bash
dotnet sln add tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj
```

- [ ] **Step 3: Write `LibobjcSmokeTests.cs`** (gates the rest):

```csharp
using Agibuild.Fulora.Platforms.Macios.Interop;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class LibobjcSmokeTests
{
    [Fact]
    public void objc_getClass_resolves_NSObject()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var cls = Libobjc.objc_getClass("NSObject");
        Assert.NotEqual(IntPtr.Zero, cls);
    }

    [Fact]
    public void sel_getUid_resolves_alloc()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var sel = Libobjc.sel_getUid("alloc");
        Assert.NotEqual(IntPtr.Zero, sel);
    }
}
```

- [ ] **Step 4: Write `NSStringRoundtripTests.cs`** (proves NSString init + UTF8String):

```csharp
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class NSStringRoundtripTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("中文 unicode 🚀")]
    public void Roundtrip_preserves_value(string input)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var s = NSString.From(input);
        Assert.Equal(input, s.ToString());
    }
}
```

- [ ] **Step 5: Write `NSObjectLifecycleTests.cs`** (no managed-self leaks):

```csharp
using Agibuild.Fulora.Platforms.Macios.Interop;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.Interop;

[Trait("Platform", "macOS")]
public class NSObjectLifecycleTests
{
    [Fact]
    public void Dispose_releases_handle()
    {
        if (!OperatingSystem.IsMacOS()) return;
        IntPtr handleSeen;
        using (var s = NSString.From("x"))
        {
            handleSeen = s.Handle;
            Assert.NotEqual(IntPtr.Zero, handleSeen);
        }
        // After Dispose, the managed wrapper must have released its retain.
        // Direct verification of native refcount is non-trivial in xUnit;
        // we settle for "no exception thrown" as smoke.
    }
}
```

> **Note on `BlockLiteralInvokeTests`:** Block invocation is the highest-risk interop primitive (ABI varies by arch). It is tested end-to-end in Task 9 (`WKHTTPCookieStoreTests` — async API uses blocks heavily) and in the Phase 0 spike (see "Phase 0 — Spikes" before Phase 1). Do not author a synthetic block test here; either use a real WebKit async API or skip.

- [ ] **Step 6: Run tests on macOS**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --nologo
```
Expected: All tests pass on macOS host.

> **⚠ CI configuration requirement:** the `if (!OperatingSystem.IsMacOS()) return;` early-return pattern means tests **silently pass** on non-macOS hosts (they don't appear as "skipped" in xUnit). To prevent illusory coverage:
>
> **Adopt option (3): CI runs `Agibuild.Fulora.Platforms.UnitTests` only on the `build-macos` job.** This is the only option that prevents silent passes on Linux/Windows hosts without fragile `--filter` syntax or MSBuild conditional `TargetFrameworks` (which has its own foot-guns around restore/lock files). Concretely: in `.github/workflows/ci.yml`, the Linux + Windows test steps use a project list that excludes `Agibuild.Fulora.Platforms.UnitTests`; the `build-macos` step explicitly includes it. Document this choice in the test csproj's header comment so a future maintainer doesn't add it to a cross-platform matrix expecting it to work.

- [ ] **Step 7: Commit**

```bash
git add tests/Agibuild.Fulora.Platforms.UnitTests/ Agibuild.Fulora.slnx
git commit -m "test(apple): macOS-host sanity tests for Macios.Interop foundation"
```

---

## Phase 2 — WebKit Managed Wrappers

### Task 6: WKWebView wrapper + smoke test

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebKit.cs` (vendor: protocol resolution helper)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebView.cs` (vendor + extend)
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKWebViewSmokeTests.cs`

- [ ] **Step 1: Vendor `WKWebKit.cs` and `WKWebView.cs`** following the Task 2 SPDX/namespace pattern.

- [ ] **Step 2: Extend `WKWebView.cs`** with the methods Fulora's adapters use that Avalonia does not (consult `src/Agibuild.Fulora.Platforms/MacOS/Native/WkWebViewShim.mm` for the full list — search for `objc_msgSend` style calls or any `webView <selector>`):

  - `LoadHTMLString(string html, NSURL? baseUrl)`
  - `EvaluateJavaScriptAsync(string script)` returning `Task<NSObject?>`
  - `Reload()`, `Stop()`, `GoBack()`, `GoForward()`
  - `CanGoBack`, `CanGoForward`, `EstimatedProgress`, `Url` properties
  - `IsHidden`, `Hidden` setter
  - `SetMagnification(double, CGPoint)` (macOS only — gate by `OperatingSystem.IsMacOS()`)

- [ ] **Step 3: Write `WKWebViewSmokeTests.cs`**:

```csharp
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios.WebKit;

[Trait("Platform", "macOS")]
public class WKWebViewSmokeTests
{
    [Fact]
    public void Init_with_default_configuration_succeeds()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var config = WKWebViewConfiguration.Create();
        using var webView = new WKWebView(config);
        Assert.NotEqual(IntPtr.Zero, webView.Handle);
    }
}
```

- [ ] **Step 4: Run on macOS**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~WKWebViewSmoke"
```
Expected: pass on macOS host.

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/{WKWebKit,WKWebView}.cs \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKWebViewSmokeTests.cs
git commit -m "feat(apple): managed WKWebView wrapper + smoke"
```

---

### Task 7: WKWebViewConfiguration / WKPreferences / WKWebpagePreferences

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebViewConfiguration.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKPreferences.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebpagePreferences.cs`

Each is a thin `NSObject` subclass exposing the properties used by Fulora adapters (audit `WkWebViewShim.mm` for the exact set: at minimum `JavaScriptEnabled`, `AllowsInlineMediaPlayback`, `MediaTypesRequiringUserActionForPlayback`, `WebsiteDataStore`, `UserContentController`).

- [ ] **Step 1: Audit `.mm` for exact property surface**

```bash
rg -n "configuration\.|preferences\.|webpagePreferences\." \
   src/Agibuild.Fulora.Platforms/MacOS/Native/WkWebViewShim.mm \
   src/Agibuild.Fulora.Adapters.iOS/Native/WkWebViewShim.iOS.mm
```
Note the union of properties touched.

- [ ] **Step 2: Author each wrapper.** Pattern (showing `WKPreferences`):

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKPreferences : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("WKPreferences");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_init = Libobjc.sel_getUid("init");
    private static readonly IntPtr s_setJavaScriptEnabled = Libobjc.sel_getUid("setJavaScriptEnabled:");

    public WKPreferences() : base(NewInstance()) { }

    private static IntPtr NewInstance()
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        return Libobjc.intptr_objc_msgSend(allocated, s_init);
    }

    public bool JavaScriptEnabled
    {
        set => Libobjc.void_objc_msgSend_bool(Handle, s_setJavaScriptEnabled, value);
    }
    // ... rest of properties from Step 1 audit
}
```

- [ ] **Step 3: Extend `WKWebViewSmokeTests.cs`** to construct configuration with non-default preferences and assert no crash.

- [ ] **Step 4: Build + test on macOS**

```bash
dotnet build src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj -c Release
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~WKWebViewSmoke"
```
Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/{WKWebViewConfiguration,WKPreferences,WKWebpagePreferences}.cs \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKWebViewSmokeTests.cs
git commit -m "feat(apple): managed WKWebViewConfiguration/Preferences/WebpagePreferences"
```

---

### Task 8: WKUserContentController

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKUserContentController.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKUserScript.cs`

Surface: `AddUserScript(WKUserScript)`, `RemoveAllUserScripts()`, `AddScriptMessageHandler(IntPtr handler, NSString name)`, `RemoveScriptMessageHandlerForName(NSString name)`.

Steps follow the Task 7 pattern: audit `.mm` for exact selectors used, author wrapper, smoke-test on macOS host (construct + invoke without WKWebView attached just to prove method dispatch).

Commit with message: `feat(apple): managed WKUserContentController + WKUserScript`.

---

### Task 9: WKWebsiteDataStore + WKHTTPCookieStore + NSHTTPCookie

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebsiteDataStore.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSHTTPCookieStore.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSHTTPCookie.cs` (vendor from Avalonia + extend)

Surface required by current `ICookieAdapter`:
- `WKWebsiteDataStore.DefaultDataStore` / `NonPersistentDataStore` (factory)
- `WKWebsiteDataStore.HttpCookieStore` (property)
- `WKHTTPCookieStore.GetAllCookiesAsync()` returning `Task<IReadOnlyList<NSHTTPCookie>>`
- `WKHTTPCookieStore.SetCookieAsync(NSHTTPCookie)`
- `WKHTTPCookieStore.DeleteCookieAsync(NSHTTPCookie)`
- `NSHTTPCookie.From(WebViewCookie)` and `NSHTTPCookie.ToWebViewCookie()` conversion

The async surfaces are implemented via `BlockLiteral` callbacks bridged to `TaskCompletionSource<T>`.

- [ ] **Step 1: Vendor `NSHTTPCookie.cs`** + add convert-from/to-`WebViewCookie` (Fulora type from `Agibuild.Fulora.Core/WebViewRecords.cs`).

- [ ] **Step 2: Author `NSHTTPCookieStore.cs` + `WKWebsiteDataStore.cs`** with the surface above.

- [ ] **Step 3: Author tests** in `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKHTTPCookieStoreTests.cs`:

```csharp
[Fact]
public async Task Set_then_Get_round_trips_cookie()
{
    if (!OperatingSystem.IsMacOS()) return;
    using var store = WKWebsiteDataStore.NonPersistentDataStore();
    var cookies = store.HttpCookieStore;
    var c = NSHTTPCookie.From(new WebViewCookie("name", "value", "example.invalid", "/", null, false, false));
    await cookies.SetCookieAsync(c);
    var all = await cookies.GetAllCookiesAsync();
    Assert.Contains(all, x => x.Name == "name" && x.Value == "value");
}
```

- [ ] **Step 4: Run tests on macOS**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~WKHTTPCookieStore"
```
Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKWebsiteDataStore.cs \
        src/Agibuild.Fulora.Platforms/Macios/Interop/Foundation/NSHTTPCookie*.cs \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKHTTPCookieStoreTests.cs
git commit -m "feat(apple): managed WKWebsiteDataStore + NSHTTPCookieStore round-trip"
```

---

### Task 10: WKURLSchemeHandler / WKURLSchemeTask managed surface

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKURLSchemeHandler.cs` (proxies to user delegate)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKURLSchemeTask.cs` (response/finish surface)

Surface required:
- `WKURLSchemeTask.DidReceiveResponse(NSURLResponse)`
- `WKURLSchemeTask.DidReceiveData(NSData)`
- `WKURLSchemeTask.DidFinish()`
- `WKURLSchemeTask.DidFailWithError(NSError)`
- `WKURLSchemeTask.Request` (property)

The runtime-registered `WKURLSchemeHandler` delegate class is built in **Task 14**; this task is the managed wrapper for the *task* type.

Commit: `feat(apple): managed WKURLSchemeTask surface`.

---

## Phase 3 — Delegates (runtime-registered ObjC classes)

### Task 11: WKNavigationDelegate runtime class (5 existing methods)

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WkDelegateBase.cs` (shared helpers)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKNavigationDelegate.cs` (vendor + extend to 5 methods)
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKNavigationDelegateTests.cs`

Five methods to register (mirrors the existing `.mm` surface):
1. `webView:decidePolicyForNavigationAction:decisionHandler:` (with block dispatch)
2. `webView:didFinishNavigation:`
3. `webView:didFailProvisionalNavigation:withError:` (carries SSL error code via NSError)
4. `webView:didFailNavigation:withError:`
5. `webView:decidePolicyForNavigationResponse:decisionHandler:`

`WKNavigationDelegate` exposes events `DidFinishNavigation`, `DidFailProvisionalNavigation`, `DecidePolicyForNavigationAction`, `DecidePolicyForNavigationResponse`. Event args carry the strongly-typed `NSURLRequest` / `NSError` / etc.

- [ ] **Step 1: Vendor + extend `WKNavigationDelegate.cs`**

Start from the Avalonia 2-method version, add the 3 missing methods (provisional fail, did-fail, decide-policy-for-response). The runtime class registration block looks like:

```csharp
static WKNavigationDelegate()
{
    var cls = AllocateClassPair("ManagedWKNavigationDelegate");
    var protocol = WebKit.objc_getProtocol("WKNavigationDelegate");
    Libobjc.class_addProtocol(cls, protocol);

    Libobjc.class_addMethod(cls,
        Libobjc.sel_getUid("webView:didFinishNavigation:"),
        s_didFinish, "v@:@@");
    Libobjc.class_addMethod(cls,
        Libobjc.sel_getUid("webView:decidePolicyForNavigationAction:decisionHandler:"),
        s_decidePolicy, "v@:@@@");
    Libobjc.class_addMethod(cls,
        Libobjc.sel_getUid("webView:didFailProvisionalNavigation:withError:"),
        s_didFailProvisional, "v@:@@@");
    Libobjc.class_addMethod(cls,
        Libobjc.sel_getUid("webView:didFailNavigation:withError:"),
        s_didFail, "v@:@@@");
    Libobjc.class_addMethod(cls,
        Libobjc.sel_getUid("webView:decidePolicyForNavigationResponse:decisionHandler:"),
        s_decideResponse, "v@:@@@");

    RegisterManagedMembers(cls);
    Libobjc.objc_registerClassPair(cls);
    s_class = cls;
}
```

Each `[UnmanagedCallersOnly]` static method reads managed self via `ReadManagedSelf<WKNavigationDelegate>(self)` and dispatches the matching event.

- [ ] **Step 2: Write `WKNavigationDelegateTests.cs`**

This test cannot mock WKWebView's navigation engine in a unit test cleanly — instead, it asserts that the runtime class is registered correctly and the registered selectors are findable:

```csharp
[Fact]
public void Registered_class_responds_to_all_selectors()
{
    if (!OperatingSystem.IsMacOS()) return;
    using var del = new WKNavigationDelegate();
    var cls = Libobjc.object_getClass(del.Handle);
    foreach (var sel in new[] {
        "webView:didFinishNavigation:",
        "webView:decidePolicyForNavigationAction:decisionHandler:",
        "webView:didFailProvisionalNavigation:withError:",
        "webView:didFailNavigation:withError:",
        "webView:decidePolicyForNavigationResponse:decisionHandler:" })
    {
        Assert.True(Libobjc.class_respondsToSelector(cls, Libobjc.sel_getUid(sel)),
            $"missing selector: {sel}");
    }
}
```

- [ ] **Step 3: Run on macOS**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~WKNavigationDelegate"
```
Expected: pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/{WkDelegateBase,WKNavigationDelegate}.cs \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKNavigationDelegateTests.cs
git commit -m "feat(apple): managed WKNavigationDelegate (5-method surface)"
```

---

### Task 12: WKUIDelegate runtime class

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKUIDelegate.cs`
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKUIDelegateTests.cs`

Methods to register (audit `.mm` for the exact set; minimum):
1. `webView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:` (macOS 12+, iOS 15+)
2. `webView:runJavaScriptAlertPanelWithMessage:initiatedByFrame:completionHandler:`
3. `webView:runJavaScriptConfirmPanelWithMessage:initiatedByFrame:completionHandler:`
4. `webView:runJavaScriptTextInputPanelWithPrompt:defaultText:initiatedByFrame:completionHandler:`

Same registration pattern as Task 11. **Tests must include both selector-presence and runtime dispatch** (matching the bar in Task 13/14): construct a WKWebView with the UIDelegate attached, evaluate JS that triggers a `confirm()` panel, assert the managed event fires with the correct message string. Selector-presence-only is **not sufficient** for this task.

Commit: `feat(apple): managed WKUIDelegate (media permission + JS panels) with end-to-end dispatch tests`.

---

### Task 13: WKScriptMessageHandler runtime class

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKScriptMessageHandler.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKScriptMessage.cs` (managed wrapper for the message struct)

One method: `userContentController:didReceiveScriptMessage:`. Extracts `message.name`, `message.body` (NSString / NSDictionary / NSArray / NSNumber), `message.frameInfo`, `message.world`. Dispatches a managed event with strongly-typed args.

Tests in `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/WebKit/WKScriptMessageHandlerTests.cs` build a content controller, attach the handler, then evaluate JS that calls `webkit.messageHandlers.<name>.postMessage(...)`, await the event.

Commit: `feat(apple): managed WKScriptMessageHandler with end-to-end JS test`.

---

### Task 14: WKURLSchemeHandler runtime class

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKURLSchemeHandlerImpl.cs` (impl name to avoid collision with the wrapper from T10)

Methods:
1. `webView:startURLSchemeTask:`
2. `webView:stopURLSchemeTask:`

Dispatches `StartTask` / `StopTask` events with `WKURLSchemeTask` managed wrapper. Production code on the adapter side replies via `task.DidReceiveResponse(...)` etc.

Test: register a custom scheme `agibuild://test/`, navigate WKWebView to it, observe `StartTask` raised, reply with HTML, observe page loaded.

Commit: `feat(apple): managed WKURLSchemeHandler with custom-scheme test`.

---

### Phase 3 Exit Gate — AOT publish smoke (early validation, NOT a substitute for Task 30)

**Files:**
- No new source files. CI smoke only.

After T11–T15 all delegate runtime classes are registered. **Before** continuing to Phase 4, run an AOT publish smoke against a tiny iOS test app that constructs `WKNavigationDelegate` + `WKUIDelegate` + `WKScriptMessageHandler`:

- [ ] **Step 1: Run AOT publish**

> **AMENDED 2026-04-25 (Spike 0c finding):** `dotnet publish` rejects `iossimulator-*` RIDs in `Microsoft.iOS` SDK ≥ `26.2.10233` (`Xamarin.Shared.Sdk.Publish.targets` requires a device architecture). For **simulator AOT smoke**, use `dotnet build` instead — it invokes the same `mono-aot-cross` toolchain and produces a launchable `.app` containing `*.aotdata.arm64`:

```bash
# Simulator AOT smoke (use dotnet build, not dotnet publish — see caveat above):
dotnet build src/Agibuild.Fulora.Adapters.iOS/Agibuild.Fulora.Adapters.iOS.csproj \
  -c Release -f net10.0-ios -p:PublishAot=true -p:RuntimeIdentifier=iossimulator-arm64
```

Expected: succeeds with at most existing IL warnings already present in the repo's baseline. New IL3xxx / IL2xxx warnings introduced by `Macios/` MUST be fixed in this gate, not deferred to Task 30. (Spike 0c verified: 0 IL warnings, AOT toolchain green, dynamic class registration via `objc_allocateClassPair` + `class_addMethod` survives AOT.)

- [ ] **Step 2: Smoke run on Simulator**

Boot iOS Simulator, install + launch the published app, observe `Macios.Interop` initialization succeeds without exception.

- [ ] **Step 3: If failures**

If AOT introduces unfixable warnings or simulator dispatch fails, **stop**. The delegate registration approach has an AOT incompatibility that Spike 0c missed. Escalate to human; do not continue Phase 4 until resolved.

- [ ] **Step 4: Commit annotations if any were added**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/
git commit -m "build(apple): AOT-clean delegate registration (Phase 3 exit gate)"
```

---

### Task 15: WKDownloadDelegate runtime class

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKDownloadDelegate.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKDownload.cs`

Methods (macOS 11.3+, iOS 14.5+):
1. `download:decideDestinationUsingResponse:suggestedFilename:completionHandler:`
2. `download:didFailWithError:resumeData:`
3. `downloadDidFinish:`

Gate the entire delegate construction at runtime by checking `OperatingSystem.IsMacOSVersionAtLeast(11, 3)` etc.; on lower OS versions the property is null and downloads are unsupported (matches `.mm` behavior).

Commit: `feat(apple): managed WKDownloadDelegate (gated by OS version)`.

---

## Phase 4 — Security framework + SSL P-2

### Task 16: Security.framework P/Invoke + SecTrust / SecCertificate / X.509 metadata extractor

**Files:**
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Security/Security.cs` (P/Invoke surface)
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Security/SecTrust.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Security/SecCertificate.cs`
- Create: `src/Agibuild.Fulora.Platforms/Macios/Interop/Security/X509MetadataExtractor.cs`
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/X509MetadataExtractorTests.cs`

- [ ] **Step 1: P/Invoke surface (`Security.cs`)**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal static partial class Security
{
    private const string Lib = "/System/Library/Frameworks/Security.framework/Security";

    [LibraryImport(Lib)]
    internal static partial IntPtr SecTrustCopyCertificateChain(IntPtr trust);

    [LibraryImport(Lib)]
    internal static partial IntPtr SecCertificateCopySubjectSummary(IntPtr cert);

    [LibraryImport(Lib)]
    internal static partial IntPtr SecCertificateCopyData(IntPtr cert);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SecTrustEvaluateWithError(IntPtr trust, out IntPtr cfErrorOut);

    // CoreFoundation helpers (for releasing CFTypeRef returned above)
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(IntPtr cf);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFDataGetBytePtr(IntPtr data);
}
```

- [ ] **Step 2: `SecCertificate.cs` + `SecTrust.cs`** as `IDisposable` wrappers (`CFRelease` in `Dispose`).

- [ ] **Step 3: `X509MetadataExtractor.cs`**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Security.Cryptography.X509Certificates;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal static class X509MetadataExtractor
{
    /// <summary>
    /// Extracts subject DN, issuer DN, NotBefore, and NotAfter from a leaf
    /// SecCertificate by copying its DER bytes into <see cref="X509Certificate2"/>.
    /// </summary>
    public static (string Subject, string Issuer, DateTimeOffset NotBefore, DateTimeOffset NotAfter)
        Extract(SecCertificate leaf)
    {
        var derBytes = leaf.CopyDer();
        using var x509 = new X509Certificate2(derBytes);
        return (x509.Subject, x509.Issuer, x509.NotBefore, x509.NotAfter);
    }
}
```

- [ ] **Step 4: Tests** — round-trip a known certificate (use an embedded resource `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/Resources/test-cert.cer`):

```csharp
[Fact]
public void Extract_returns_subject_issuer_validity_for_known_cert()
{
    if (!OperatingSystem.IsMacOS()) return;
    var derBytes = File.ReadAllBytes("Macios/Security/Resources/test-cert.cer");
    using var cert = SecCertificate.FromDer(derBytes);
    var (subject, issuer, notBefore, notAfter) = X509MetadataExtractor.Extract(cert);
    Assert.Contains("CN=", subject);
    Assert.True(notAfter > notBefore);
}
```

Generate the test certificate with `openssl req -x509 -newkey rsa:2048 -nodes -keyout /dev/null -out tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/Resources/test-cert.cer -days 365 -outform DER -subj "/CN=fulora-test"`.

> Use **embedded resource** loading (not `File.ReadAllBytes` against a relative path) so the test is robust against test-runner working directory differences. In the csproj:
>
> ```xml
> <ItemGroup>
>   <EmbeddedResource Include="Macios/Security/Resources/test-cert.cer" />
> </ItemGroup>
> ```
>
> And in the test, load via `typeof(X509MetadataExtractorTests).Assembly.GetManifestResourceStream(...)`.

- [ ] **Step 5: Run on macOS + commit**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~X509MetadataExtractor"
git add src/Agibuild.Fulora.Platforms/Macios/Interop/Security/ \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/
git commit -m "feat(apple): Security.framework SecTrust/SecCertificate + X509 extractor"
```

---

### Task 17: WKNavigationDelegate.didReceiveAuthenticationChallenge override + full ServerCertificateErrorContext

**Files:**
- Modify: `src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKNavigationDelegate.cs:lines TBD` (add 6th method)
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/ServerTrustChainTests.cs`

- [ ] **Step 1: Add 6th method to runtime class** in `WKNavigationDelegate.cs`:

```csharp
private static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>
    s_didReceiveChallenge = &OnDidReceiveAuthChallenge;

// ... in static ctor ...
Libobjc.class_addMethod(cls,
    Libobjc.sel_getUid("webView:didReceiveAuthenticationChallenge:completionHandler:"),
    s_didReceiveChallenge, "v@:@@@");

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static void OnDidReceiveAuthChallenge(
    IntPtr self, IntPtr sel, IntPtr webView, IntPtr challenge, IntPtr completionHandler)
{
    var managed = ReadManagedSelf<WKNavigationDelegate>(self);
    if (managed?.DidReceiveServerTrustChallenge is null)
    {
        // No subscriber → defer to system default. NSURLSessionAuthChallengePerformDefaultHandling = 1
        InvokeBlock(completionHandler, 1, IntPtr.Zero);
        return;
    }

    var args = managed.BuildServerTrustEventArgs(challenge);
    if (args is null)
    {
        // Not a server trust challenge → default
        InvokeBlock(completionHandler, 1, IntPtr.Zero);
        return;
    }

    managed.DidReceiveServerTrustChallenge.Invoke(managed, args);

    // The decision is owned by Agibuild.Fulora.Security.INavigationSecurityHooks on
    // the adapter side (see Task 19/20). The current default is
    // DefaultNavigationSecurityHooks → NavigationSecurityDecision.Reject; we therefore
    // unconditionally cancel here. If a future security policy adds a Proceed path,
    // BOTH the hook signature AND this completion handler must change in lockstep —
    // this delegate must NOT speculate or branch on hook output.
    // NSURLSessionAuthChallengeCancelAuthenticationChallenge = 2
    InvokeBlock(completionHandler, 2, IntPtr.Zero);
}
```

- [ ] **Step 2: `BuildServerTrustEventArgs`** extracts `SecTrust` from `challenge.protectionSpace.serverTrust`, takes the leaf certificate (index 0) of `SecTrustCopyCertificateChain`, and constructs:

```csharp
public sealed class ServerTrustChallengeEventArgs : EventArgs
{
    public required string Host { get; init; }
    public required string ErrorSummary { get; init; }
    public required int PlatformRawCode { get; init; }
    public string? CertificateSubject { get; init; }
    public string? CertificateIssuer { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidTo { get; init; }
}
```

The adapter side (Task 19/20) maps this to `ServerCertificateErrorContext` and invokes `INavigationSecurityHooks`.

- [ ] **Step 3: Tests** in `ServerTrustChainTests.cs` — classified as **integration** (`[Trait("Category", "Integration")]`, not unit), runs on macOS host only initially. iOS Simulator support is a follow-up step in Task 28 because the simulator needs `xcrun simctl keychain add-root-cert` setup that's not part of this task. Start a local TLS server using a self-signed cert, navigate WKWebView there, assert `DidReceiveServerTrustChallenge` raised with all four optional fields populated. Set a 10-second timeout on the test:

```csharp
[Fact]
public async Task SelfSigned_trust_raises_event_with_full_certificate_metadata()
{
    if (!OperatingSystem.IsMacOS()) return;
    using var server = await SelfSignedHttpsTestServer.StartAsync();
    using var config = WKWebViewConfiguration.Create();
    using var webView = new WKWebView(config);
    using var nav = new WKNavigationDelegate();
    webView.NavigationDelegate = nav;
    var tcs = new TaskCompletionSource<ServerTrustChallengeEventArgs>();
    nav.DidReceiveServerTrustChallenge += (_, e) => tcs.TrySetResult(e);
    webView.LoadRequest(NSURLRequest.From(server.Uri));
    var args = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.Equal(server.Uri.Host, args.Host);
    Assert.NotNull(args.CertificateSubject);
    Assert.NotNull(args.CertificateIssuer);
    Assert.NotNull(args.ValidFrom);
    Assert.NotNull(args.ValidTo);
}
```

`SelfSignedHttpsTestServer` lives under `tests/Agibuild.Fulora.Platforms.UnitTests/Helpers/` and starts an `HttpListener` (or `Kestrel`) with a self-signed cert.

- [ ] **Step 4: Run + commit**

```bash
dotnet test tests/Agibuild.Fulora.Platforms.UnitTests/Agibuild.Fulora.Platforms.UnitTests.csproj -c Release --filter "FullyQualifiedName~ServerTrustChain"
git add src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKNavigationDelegate.cs \
        tests/Agibuild.Fulora.Platforms.UnitTests/Macios/Security/ \
        tests/Agibuild.Fulora.Platforms.UnitTests/Helpers/
git commit -m "feat(apple): WKNavigationDelegate didReceiveAuthenticationChallenge with full cert metadata"
```

---

### Task 18: AdapterSslRejectionContract Apple slice upgrade to P-2

**Files:**
- Modify: `tests/Agibuild.Fulora.UnitTests/Security/AdapterSslRejectionContract.cs:lines TBD` (from predecessor plan)
- Create: `tests/Agibuild.Fulora.UnitTests/Security/AdapterSslRejectionContractAppleSliceTests.cs`

The predecessor plan's contract asserts `Host` / `ErrorSummary` / `PlatformRawCode` always populated. This task adds an *opt-in* assertion set "PlatformProvidesCertificateMetadata" that platforms can satisfy. Apple opts in starting from this plan.

- [ ] **Step 1: Extend contract**

```csharp
public static async Task PlatformProvidesCertificateMetadata_when_supported(
    AdapterFactory factory, bool platformSupportsMetadata)
{
    using var adapter = factory();
    // ... trigger SSL error ...
    var ex = await CaptureFailureAsync(adapter);
    if (platformSupportsMetadata)
    {
        Assert.NotNull(ex.CertificateSubject);
        Assert.NotNull(ex.CertificateIssuer);
        Assert.NotNull(ex.ValidFrom);
        Assert.NotNull(ex.ValidTo);
    }
    else
    {
        // Android/Windows pre-Phase-4 MAY return null; not an assertion failure.
    }
}
```

- [ ] **Step 2: Apple-slice runner** (`AdapterSslRejectionContractAppleSliceTests.cs`) calls the contract with `platformSupportsMetadata: true` against the Apple adapter (via integration test fixture).

- [ ] **Step 3: Run + commit**

Commit: `test(security): apple slice asserts full server cert metadata under SSL P-2`.

---

## Phase 5 — Adapter Cutover

### Task 19: macOS adapter cutover

**Files:**
- Modify: `src/Agibuild.Fulora.Platforms/MacOS/MacOSWebViewAdapter.cs` (rewrite body — keep public surface)
- Modify: `src/Agibuild.Fulora.Platforms/MacOS/MacOSWebViewAdapter.PInvoke.cs` (delete file in this commit)

> **AMENDED 2026-04-25 (Spike 0a PASS):** Step 1 below is REMOVED. macOS managed code stays on the existing `net10.0` TFM with `[SupportedOSPlatform("macos")]` runtime gating, mirroring the current `MacOSWebViewAdapter` pattern. No `EnableMacOSTfm` flag, no `net10.0-macos` TFM, no Platforms csproj edit needed in this task. Renumber the surviving steps accordingly when implementing.

- [~] ~~**Step 1: Add `net10.0-macos` TFM**~~ — **REMOVED per Spike 0a (2026-04-25)**. `net10.0` + runtime gate is sufficient; Apple slice does not need a dedicated macOS TFM. See [`2026-04-25-spike-results.md`](../plans/2026-04-25-spike-results.md) § Spike 0a for evidence (6/6 probes including `dlopen libobjc`, `objc_getClass(WKWebView)`, `dlopen Security.framework` succeeded under `net10.0`).

- [ ] **Step 2: Rewrite `MacOSWebViewAdapter.cs`** body to use the new `Macios/` namespace types instead of P/Invoke into `libAgibuildWebViewWk`. Adapter public methods (`InitializeAsync`, `NavigateAsync`, `EvaluateScriptAsync`, etc.) stay byte-identical; only the implementation changes.

  **Ownership / lifetime contract (must be enforced in this rewrite):**
  - The adapter holds **strong references** to `WKWebView`, `WKNavigationDelegate`, `WKUIDelegate`, `WKScriptMessageHandler`, `WKURLSchemeHandlerImpl`, and `WKDownloadDelegate` for its entire lifetime.
  - Each managed delegate's `RegisterManagedMembers` allocation MUST be released in the corresponding `dealloc` selector implementation; the adapter's `Dispose` triggers WKWebView teardown which triggers each delegate's `dealloc` which releases the GCHandle. Test this with a leak detector (count `GC.GetTotalMemory(true)` before + after 100 adapter cycles).
  - The `INavigationSecurityHooks` instance is injected via constructor (default = `DefaultNavigationSecurityHooks.Instance`); the SSL event handler captures it explicitly as a field, never via closure over `this`, to avoid lifetime ambiguity.

  Key transformations:
  - Replace `LibWk.create(...)` with `var webView = new WKWebView(config)`
  - Replace `LibWk.set_navigation_callbacks(...)` with `webView.NavigationDelegate.DidFinishNavigation += ...`
  - Replace `LibWk.eval_js(...)` with `await webView.EvaluateJavaScriptAsync(...)`
  - SSL error handling routes through `WKNavigationDelegate.DidReceiveServerTrustChallenge` (Task 17) → builds `ServerCertificateErrorContext` (full fields) → `_securityHooks.OnServerCertificateError(...)` → `RaiseNavigationCompleted(Failure, new WebViewSslException(context, navigationId))`

- [ ] **Step 3: Delete `MacOSWebViewAdapter.PInvoke.cs`** (the LibWk interop is gone).

- [ ] **Step 4: Build + smoke**

```bash
dotnet build src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj -c Release --nologo -p:EnableMacOSTfm=true
```
Expected: 0 errors / 0 warnings on both `net10.0` and `net10.0-macos`.

- [ ] **Step 5: Run macOS integration tests**

```bash
dotnet test tests/Agibuild.Fulora.Integration.Tests/Agibuild.Fulora.Integration.Tests.csproj -c Release --filter "Category=MacOS"
```
Expected: full pass.

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Platforms/MacOS/ \
        src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj
git rm src/Agibuild.Fulora.Platforms/MacOS/MacOSWebViewAdapter.PInvoke.cs
git commit -m "feat(apple): cut macOS adapter over to managed Macios namespace"
```

(No `!` BREAKING marker — adapter public surface is unchanged. The `[2.0.0]` CHANGELOG entry in Task 31 documents the build-pipeline impact for downstream forks; that's the appropriate level of disclosure.)

---

### Task 20: iOS adapter cutover

**Files:**
- Modify: `src/Agibuild.Fulora.Adapters.iOS/iOSWebViewAdapter.cs` (rewrite body)
- Modify: `src/Agibuild.Fulora.Adapters.iOS/Agibuild.Fulora.Adapters.iOS.csproj` (add link items)

- [ ] **Step 1: Link Macios sources** into the iOS csproj:

```xml
<ItemGroup>
  <Compile Include="..\Agibuild.Fulora.Platforms\Macios\**\*.cs"
           LinkBase="Macios" />
</ItemGroup>
```

- [ ] **Step 2: Rewrite `iOSWebViewAdapter.cs`** body following the Task 19 transformation pattern. iOS-specific differences:
  - Host view is `UIView` (not `NSView`)
  - No `WKWebView.SetMagnification` (gate at compile time)
  - Download delegate gated on `OperatingSystem.IsIOSVersionAtLeast(14, 5)`

- [ ] **Step 3: Build for `net10.0-ios`**

```bash
dotnet build src/Agibuild.Fulora.Adapters.iOS/Agibuild.Fulora.Adapters.iOS.csproj -c Release --nologo
```
Expected: 0 errors. (Workload required: `dotnet workload list | grep ios`.)

- [ ] **Step 4: iOS simulator integration regression**

```bash
dotnet test tests/Agibuild.Fulora.Integration.Tests/Agibuild.Fulora.Integration.Tests.iOS/ -c Release --filter "Category=iOS"
```
Expected: full pass. (If iOS Simulator test wiring is not yet present in `nuke Ci`, Task 28 covers adding it; this step assumes local execution on a macOS host with Simulator installed.)

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Adapters.iOS/
git commit -m "feat(apple): cut iOS adapter over to managed Macios namespace"
```

---

### Task 21: Cookie / Download / Scheme / Permission high-level surface verification

**Files:**
- No new files. Verification only.

- [ ] **Step 1: Run the full Apple integration suite end-to-end**

```bash
nuke LocalPreflight -- platform-filter=apple
```
Expected: all Apple tests pass (cookies, downloads, custom scheme, media permission).

- [ ] **Step 2: Commit nothing if no test changes; otherwise commit fix(apple): regression test adjustments.**

---

## Phase 6 — Cleanup

### Task 22: Delete macOS `Native/` shim

**Files:**
- Delete: `src/Agibuild.Fulora.Platforms/MacOS/Native/` (entire folder)

- [ ] **Step 1: Confirm zero references**

```bash
rg -l "WkWebViewShim|libAgibuildWebViewWk" src/ tests/ build/
```
Expected: no matches in `src/` or `tests/` (only in this plan doc and CHANGELOG).

- [ ] **Step 2: Delete folder**

```bash
git rm -r src/Agibuild.Fulora.Platforms/MacOS/Native/
```

- [ ] **Step 3: Build + commit**

```bash
dotnet build -c Release --nologo
git commit -m "chore(apple): delete obsolete macOS .mm shim and native artifacts"
```

---

### Task 23: Delete iOS `Native/` shim

**Files:**
- Delete: `src/Agibuild.Fulora.Adapters.iOS/Native/` (entire folder, includes `.mm` / `.a` / `.o` / `.xcframework`)

Same pattern as Task 22. Commit: `chore(apple): delete obsolete iOS .mm shim and native artifacts`.

---

### Task 24: Remove Nuke shim build pipeline

**Files:**
- Modify: `build/Build.cs`, `build/Build.Helpers.cs`, `build/Build.Platform.cs` (search for shim-build-related callsites)
- Modify: `build/_build.csproj` if `Nuke.Common.Tools.Apple` is referenced solely for native shim build

> **Scope clarification:** Remove only invocations whose **purpose is compiling the obsolete `.mm` / `xcframework`**. Calls to `xcodebuild` / `xcrun` for iOS Simulator orchestration, iOS device deployment, or `dotnet workload`-required toolchain probes MUST remain — those are .NET iOS tooling needs, not shim build needs. Audit each callsite individually.

- [ ] **Step 1: Audit shim-build callsites**

```bash
rg -n "BuildAppleNative|WkWebViewShim|libAgibuildWebViewWk|lipo |Lipo " build/
```
For each match, classify: "shim build" (delete) vs "iOS workload tooling" (keep).

- [ ] **Step 2: Remove only shim-build entries**

- [ ] **Step 3: Build verification**

```bash
nuke LocalPreflight
```
Expected: green. Xcode-related invocations in the log are acceptable IF they originate from `dotnet workload` / iOS Simulator orchestration; they MUST NOT originate from shim build steps.

- [ ] **Step 4: Commit**

```bash
git add build/
git commit -m "chore(build): remove Apple shim build pipeline (xcframework/lipo) — workload usage retained"
```

---

### Task 25: Remove Apple shim build step from CI (and audit for orphaned signing/notarization)

**Files:**
- Modify: `.github/workflows/ci.yml` (remove "Build Apple native shim" step)
- Modify: `.github/workflows/release.yml` if it independently builds OR signs/notarizes the shim binary

- [ ] **Step 1: Audit + classify**

```bash
rg -n "xcodebuild|WkWebViewShim|libAgibuildWebViewWk|codesign|notarytool|altool" .github/
```
Classify each match:
- **Shim build** → delete in this commit.
- **Shim signing/notarization** (e.g. `codesign` against `*.framework` or `*.dylib` from the shim) → delete in this commit; .NET-managed `Macios/Interop/*.cs` is signed transparently by the iOS workload's app bundle signing, no separate codesign step needed for the dylib that no longer exists.
- **iOS workload setup** (`setup-dotnet`, `dotnet workload install ios`, simulator runner) → keep.

- [ ] **Step 2: Remove matched shim-build + shim-signing steps**

- [ ] **Step 3: Validate workflow YAML**

```bash
gh workflow view ci --yaml > /dev/null  # or use actionlint
```

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/
git commit -m "ci(apple): drop Xcode native shim build + orphaned codesign steps"
```

---

### Task 26: Update `.gitignore`

**Files:**
- Modify: `.gitignore` (remove `*.xcframework`, `*.a`, `*.o`, `WkWebViewShim*` entries that targeted the shim)

- [ ] **Step 1: Audit + remove**

```bash
grep -n -E "xcframework|WkWebViewShim|libAgibuildWebViewWk" .gitignore
```
Remove matches.

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m "chore(gitignore): drop obsolete Apple native artifact ignores"
```

---

## Phase 7 — Verification + Release

### Task 27: LocalPreflight green + format clean + coverage ≥ baseline

**Files:**
- No new files.

- [ ] **Step 1: `nuke LocalPreflight`**

Expected: 0 errors, 0 warnings, all tests pass.

- [ ] **Step 2: `dotnet format --verify-no-changes`** (no style drift)

- [ ] **Step 3: Coverage threshold (per-area)**

```bash
nuke Coverage
```

A flat global floor of 93.14% is brittle when adding ~3000 lines of interop. Apply a per-area policy:

| Area | Floor | Rationale |
|---|---|---|
| Cross-platform `Agibuild.Fulora.Core` (existing scope) | ≥ 93.14% (existing baseline, unchanged) | Same code, no excuse to drop. |
| `Macios/Interop/Security/**` | ≥ 95% | Security-critical, fully testable on macOS host. |
| `Macios/Interop/WebKit/WKNavigationDelegate.cs` | ≥ 90% | SSL hook routing path. |
| `Macios/Interop/WebKit/**` (rest) | ≥ 75% | Some methods (e.g. `WKDownloadDelegate` gated on macOS 11.3+) may be untestable on older runners. |
| `Macios/Interop/Foundation/**` | ≥ 85% | Mostly testable on macOS host. |
| `Macios/Interop/{Libobjc,BlockLiteral,NSObject,NSManagedObjectBase}.cs` | excluded from gate | Vendored from Avalonia; coverage measured by their consumers. |

Configure these in `coverlet.runsettings` per-assembly thresholds. If `nuke Coverage` does not currently support per-path thresholds, extend it in this task.

- [ ] **Step 4: If anything fails, fix and commit before proceeding.**

---

### Task 28: macOS / iOS device + simulator full integration regression

**Files:**
- No new files.

- [ ] **Step 1: Provision macOS runner with iOS Simulator**

- [ ] **Step 2: Verify CI wiring**

Audit `build/Build.cs` `Ci` target and `.github/workflows/ci.yml` `build-macos` job to confirm both Apple integration test projects are invoked. If `Agibuild.Fulora.Integration.Tests.iOS` is missing from the CI test set, add it in this step:

```bash
rg -n "Integration\.Tests\.iOS|Integration\.Tests/.*iOS" build/ .github/
```

- [ ] **Step 3: Run integration suites locally on macOS**

```bash
dotnet test tests/Agibuild.Fulora.Integration.Tests/ -c Release --filter "Category=MacOS"
dotnet test tests/Agibuild.Fulora.Integration.Tests/Agibuild.Fulora.Integration.Tests.iOS/ -c Release --filter "Category=iOS-Simulator"
```
Expected: all pass with parity to pre-cutover baseline.

- [ ] **Step 4: Optional iOS device run** if a connected device is available; record completion only when this passes.

---

### Task 29: Stryker.NET on security-critical interop (`Security/` + `WKNavigationDelegate`)

**Files:**
- Create: `tests/Agibuild.Fulora.Platforms.UnitTests/stryker-config.json`

- [ ] **Step 1: Configure Stryker**

Stryker resolves `project` and `test-projects` relative to the directory containing the config file. Use absolute paths from repo root for `mutate` filters.

Create `tests/Agibuild.Fulora.Platforms.UnitTests/stryker-config.json`:

```json
{
  "stryker-config": {
    "project": "../../src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj",
    "test-projects": ["./Agibuild.Fulora.Platforms.UnitTests.csproj"],
    "mutate": [
      "../../src/Agibuild.Fulora.Platforms/Macios/Interop/Security/**/*.cs",
      "../../src/Agibuild.Fulora.Platforms/Macios/Interop/WebKit/WKNavigationDelegate.cs"
    ],
    "thresholds": { "high": 90, "low": 80, "break": 75 }
  }
}
```

`WKNavigationDelegate` is included because it owns the SSL hook routing path; mutation gaps there compromise the whole security policy contract.

- [ ] **Step 2: Run from repo root**

```bash
cd /Users/Hongwei.Xi/projects/Fulora
dotnet stryker --config-file tests/Agibuild.Fulora.Platforms.UnitTests/stryker-config.json
```
Expected: mutation score ≥ 80% across both files.

- [ ] **Step 3: Commit**

```bash
git add tests/Agibuild.Fulora.Platforms.UnitTests/stryker-config.json
git commit -m "test(security): stryker mutation gate ≥80% on Macios.Interop.Security + WKNavigationDelegate"
```

---

### Task 30: AOT publish smoke for `net10.0-ios`

**Files:**
- No new files. CI matrix only.

- [ ] **Step 1: Add AOT publish job to CI** (or run locally):

> **AMENDED 2026-04-25 (Spike 0c hard checkpoint):** `dotnet publish` requires a **device** RID (`ios-arm64`). Spike 0c verified the AOT toolchain on `iossimulator-arm64` via `dotnet build` (since SDK rejects publish for simulator RIDs). **Device-targeted `dotnet publish` + code signing was NOT end-to-end verified by the spike** (no physical device available). This task is therefore the **hard checkpoint** for the device publish + signing pipeline. If `dotnet publish` for `ios-arm64` fails here for reasons beyond IL warnings (signing / provisioning / runtimepack mismatches), Phase 1 must escalate immediately — do not work around with `dotnet build` for the device target.

```bash
# Device AOT publish (requires Apple Developer signing identity in keychain):
dotnet publish src/Agibuild.Fulora.Adapters.iOS/Agibuild.Fulora.Adapters.iOS.csproj \
  -c Release -f net10.0-ios -p:PublishAot=true -p:RuntimeIdentifier=ios-arm64
```
Expected: succeeds without IL3xxx / IL2xxx errors. If trim warnings appear, address with `[DynamicDependency]` annotations on managed-self callback sites — this is the AOT acceptance gate.

- [ ] **Step 2: Commit any required annotations**

```bash
git add src/Agibuild.Fulora.Platforms/Macios/
git commit -m "build(apple): AOT-clean Macios interop with dynamic-dependency annotations"
```

---

### Task 31: CHANGELOG + MIGRATION_GUIDE + framework-capabilities.json + UpdateVersion → release

**Files:**
- Modify: `CHANGELOG.md` (under new `## [2.0.0]` heading shared with sibling v2 plan)
- Modify: `docs/MIGRATION_GUIDE.md`
- Modify: `docs/framework-capabilities.json` (add `platforms.apple.shim.modernized: status=stable`)
- Modify: `docs/API_SURFACE_REVIEW.md` (note: no public surface change)
- Modify: `docs/platform-status.md` (Apple section: shim retired, pure-managed path)
- Modify: `Directory.Build.props` (`<VersionPrefix>` bumped via `nuke UpdateVersion`)

- [ ] **Step 1: Author CHANGELOG entry**

```markdown
### Apple platform — pure C# runtime delegate (no more Xcode native shim)

`src/Agibuild.Fulora.Platforms/MacOS/Native/` and
`src/Agibuild.Fulora.Adapters.iOS/Native/` removed. Apple WebView integration now
goes through `Agibuild.Fulora.Platforms.Macios.*` (pure C#, ObjC runtime API).

**Public API impact:** none. All adapter public surface (`IWebView`, `IWebViewBridge`,
`ICookieAdapter`, etc.) is byte-identical.

**Build pipeline impact:** Xcode (`xcodebuild`, `lipo`) is no longer invoked by Nuke
or CI. Apple platform builds with `dotnet build` / `dotnet publish` only.

**SSL policy:** Apple platform now reports `WebViewSslException` with full certificate
metadata (Subject / Issuer / NotBefore / NotAfter). See
`docs/superpowers/plans/2026-04-23-navigation-ssl-policy-explicit.md` for the
cross-platform contract.
```

- [ ] **Step 2: Author MIGRATION_GUIDE rows** (none for consumers; build/CI rows for downstream forks).

- [ ] **Step 3: Update `docs/framework-capabilities.json`**

```json
"platforms.apple.shim.modernized": {
  "status": "stable",
  "since": "2.0.0",
  "owner": "platforms",
  "policy": "release-gate-required"
}
```

- [ ] **Step 4: Bump version**

```bash
nuke UpdateVersion --to 2.0.0-preview.7
```
(Or whichever preview tag this plan completes — depends on cadence with sibling plan.)

- [ ] **Step 5: Final commit + push + monitor CI**

```bash
git add CHANGELOG.md docs/MIGRATION_GUIDE.md docs/framework-capabilities.json \
        docs/API_SURFACE_REVIEW.md docs/platform-status.md Directory.Build.props
git commit -m "release(2.0.0-preview.7): apple shim modernization complete"
git push origin release/v2
gh run watch --exit-status
```
Expected: CI green on all jobs (now without xcodebuild step). Tag for preview release once green.

---

## Definition of Done

- [ ] Spike 0a, 0b, 0c all passed; results recorded in `docs/superpowers/plans/2026-04-25-spike-results.md`.
- [ ] All 31 tasks in this plan have `[x]` on every step.
- [ ] `src/Agibuild.Fulora.Platforms/MacOS/Native/` deleted from repo.
- [ ] `src/Agibuild.Fulora.Adapters.iOS/Native/` deleted from repo.
- [ ] No `xcodebuild` invocation in `build/` or `.github/workflows/` **for the purpose of compiling the Apple shim** (calls to `xcrun simctl` for iOS Simulator orchestration, or `xcodebuild` for `dotnet workload`-required iOS device builds, remain allowed and expected — these are .NET iOS tooling concerns, not the shim build).
- [ ] No `*.mm` / `*.a` / `*.xcframework` / `*.o` files in tracked tree (`git ls-files | rg -i '\.(mm|a|xcframework|o)$'` is empty).
- [ ] `nuke LocalPreflight` green; per-area coverage thresholds (Task 27 Step 3 table) all met.
- [ ] Stryker mutation score ≥ 80% on **both** `Macios.Interop.Security` AND `Macios/Interop/WebKit/WKNavigationDelegate.cs` (Task 29 scope).
- [ ] Apple integration tests pass on macOS host + iOS Simulator.
- [ ] AOT publish succeeds for `net10.0-ios`.
- [ ] CHANGELOG `[2.0.0]` section has the apple-modernization entry.
- [ ] `docs/framework-capabilities.json` carries `platforms.apple.shim.modernized: status=stable, since=2.0.0`.
- [ ] At least one full release cycle on `2.0.0-rc.1` with no Apple regression report before tagging `2.0.0`.

---

## Rollback Strategy

Each Phase produces a **release-able preview tag**. If a Phase causes regression discovered after merge:

| Discovered during | Rollback action |
|---|---|
| Phase 0 spikes | Spike branch is throwaway; no rollback needed. Just don't merge. |
| Phase 1-4 | `git revert` the offending phase's commits (no consumer impact yet — adapters still on `.mm`). |
| Phase 5 (cutover) | `git revert` the cutover commit; consumers can pin to the previous preview tag (which still has the working `.mm` adapter). |
| Phase 6 (delete) | Restore deleted `Native/` folder from prior commit; revert Phase 5 cutover; re-issue preview without modernization. The cleanup phase is the **point of no return** — verify thoroughly in Phase 5 before deleting. |

### Phase 6 Entry Gate (point-of-no-return enforcement)

Phase 6 commits MUST NOT land until **all** of:

1. The **named** previous preview tag (e.g. `2.0.0-preview.7` if Phase 5 sealed at preview.7) has been published for **at least 7 days**.
2. **Zero** GitHub issues opened against that preview tag with label `area:apple` and severity `critical` or `high`.
3. The early-adopter list (recorded in `docs/superpowers/plans/2026-04-25-spike-results.md` after Phase 0; populated with the names of the at-least-2 internal teams + at-least-1 external consumer who agreed to dogfood) has provided written signoff in a GitHub Discussion thread.
4. macOS + iOS Simulator integration suites have been re-run on the named preview tag's commit SHA within the past 24 hours, all green.

If any condition fails, postpone Phase 6 by another preview cycle. Do not "soft-delete" (e.g. rename folder) as a compromise — either land the deletion fully or postpone fully.

---

## Open Questions Resolved by Phase 0 Spikes

| # | Question | Resolved by | Resolution recorded in |
|---|---|---|---|
| 1 | `net10.0-macos` TFM addition (Task 19) — needed or not? | **Spike 0a — PASS (2026-04-25)** | [`2026-04-25-spike-results.md`](./2026-04-25-spike-results.md) § Spike 0a. **Answer: not needed.** Task 19 Step 1 removed; macOS slice stays on `net10.0` + runtime gate. |
| 2 | iOS device cert pinning for SSL test (Task 17) | **Task 17 Step 3** — handled inline; test classified as integration (macOS host first, iOS Simulator second with `xcrun simctl keychain add-root-cert`). | Task 17 Step 3 commentary |
| 3 | Block invocation ABI on iOS arm64 vs macOS arm64 | **Spike 0b — PASS (2026-04-25)** | [`2026-04-25-spike-results.md`](./2026-04-25-spike-results.md) § Spike 0b. **Answer:** Avalonia `BlockLiteral` works on iOS Simulator arm64 (iOS 26.4); no iOS-specific descriptor variant needed for this probe. Physical device validation deferred to Phase 7. |
| 4 | AOT viability for `AllocateClassPair` + `class_addMethod` from C# | **Spike 0c — PASS (2026-04-25, with simulator-RID caveat)** | [`2026-04-25-spike-results.md`](./2026-04-25-spike-results.md) § Spike 0c. **Answer:** AOT toolchain (`mono-aot-cross`) green; dynamic class registration + `[UnmanagedCallersOnly]` trampoline + selector dispatch survives AOT. **Caveat:** SDK ≥ `26.2.10233` rejects `iossimulator-*` for `dotnet publish` (use `dotnet build` for sim AOT smoke); device-RID publish + signing untested in spike — hard checkpoint at Task 30. |
| 5 | `release/v2` branch carving — already done by sibling plan? | Resolved before Task 1 by checking `git branch --list release/v2`. If branch missing, Task 1 must carve it. | Task 1 commit message notes branch state. |
| 6 | Predecessor SSL P-1 branch (A vs B above) | "Predecessor Branch Decision" section above (decided **before** Task 1). Recommended: **Branch B**. | Update predecessor plan task list with `cancelled` markers if Branch B chosen. |
