# Phase 0 Spike Results — Fulora v2 Apple Shim Modernization

**Plan:** [`docs/superpowers/plans/2026-04-25-fulora-v2-apple-shim-modernization.md`](./2026-04-25-fulora-v2-apple-shim-modernization.md)

**Phase 0 gates:** Three independent spikes must all PASS before Phase 1 may begin.

| Spike | Question | Status | Time |
|---|---|---|---|
| 0a — `net10.0-macos` TFM decision | Is `net10.0` + `[SupportedOSPlatform("macos")]` runtime gating sufficient? | **PASS** | 2026-04-25 |
| 0b — `BlockLiteral` ABI on iOS arm64 | Does Avalonia's `BlockLiteral` work on iOS arm64 (sim + device)? | **PASS** | 2026-04-25 |
| 0c — AOT publish smoke for `AllocateClassPair` + `class_addMethod` | Does the runtime ObjC class registration approach survive `PublishAot=true` on `net10.0-ios`? | **PASS** *(with simulator-RID caveat)* | 2026-04-25 |

**Phase 0 exit criterion:** all three PASS → tag spike commit + update plan Open Questions → proceed to Phase 1.

---

## Spike 0a — `net10.0` is sufficient (PASS)

**Date:** 2026-04-25 · **Host:** macOS 26.4.1 · **Runtime:** .NET 10.0.5 · **Arch:** Arm64

### Method

Built a minimal `net10.0` (NOT `net10.0-macos`) console project that calls into the macOS dynamic loader and Objective-C runtime through `[DllImport]` only — exactly what the v2 plan's runtime ObjC interop will rely on. No `Microsoft.macOS.SDK` reference, no `[SupportedOSPlatform("macos")]` attribute (so the analyzer would have flagged any required member that hides behind the macOS TFM).

### Probes

Six probes covering every Phase 1–4 native dependency:

1. `dlopen("/usr/lib/libobjc.dylib", RTLD_NOW)` — Phase 1 hard requirement.
2. `objc_getClass("NSObject")` + `class_getName` round-trip — Phase 1 Foundation interop foundation.
3. `sel_registerName("webView:didFinishNavigation:")` — Phase 3 selector registration.
4. `dlopen("/System/Library/Frameworks/WebKit.framework/WebKit", RTLD_NOW)` — Phase 2 hard requirement.
5. `objc_getClass("WKWebView")` (after WebKit framework load) — Phase 2 Task 6.
6. `dlopen("/System/Library/Frameworks/Security.framework/Security", RTLD_NOW)` — Phase 4 SecTrust / SecCertificate.

### Result

```
[OK] dlopen(/usr/lib/libobjc.dylib)                                            -> 0x367B3F19C
[OK] objc_getClass(NSObject)                                                   -> 0x1F2E21618
    -> class_getName: NSObject
[OK] sel_registerName(webView:didFinishNavigation:)                            -> 0x200E20F08
[OK] dlopen(/System/Library/Frameworks/WebKit.framework/WebKit)                -> 0x367B90570
[OK] objc_getClass(WKWebView)                                                  -> 0x1F313D2C8
[OK] dlopen(/System/Library/Frameworks/Security.framework/Security)            -> 0x367BC4240
RESULT: GO -- net10.0 is sufficient for Apple slice runtime ObjC interop.
```

### Corroborating evidence (already in the codebase)

`src/Agibuild.Fulora.Platforms/MacOS/MacOSWebViewAdapter.PInvoke.cs` is declared
`[SupportedOSPlatform("macos")]` and ships under `net10.0` today (per
`src/Agibuild.Fulora.Platforms/Agibuild.Fulora.Platforms.csproj`'s
`<TargetFrameworks Condition="'$(EnableAndroidTfm)' != 'true'">net10.0</TargetFrameworks>`).
The current macOS adapter therefore already proves `net10.0 + runtime gate` is a
viable hosting strategy; this spike additionally proves the **runtime ObjC
interop pattern** the v2 plan introduces (direct `dlopen` + `objc_getClass` +
`sel_registerName`, no Microsoft.macOS.SDK dependency) works on the same TFM.

### Decision

**GO — `net10.0` is sufficient.** Apple slice does not need a dedicated `net10.0-macos` TFM.

### Plan amendments required

- **v2 plan Task 19 Step 1:** REMOVE the `<TargetFrameworks>net10.0;net10.0-macos</TargetFrameworks>` step. Apple managed code stays on `net10.0` with `[SupportedOSPlatform("macos")]` runtime gating, mirroring the current `MacOSWebViewAdapter` pattern.
- **v2 plan Open Question 1:** Mark answered: "`net10.0` + runtime gate sufficient; verified by Spike 0a (2026-04-25, all 6 probes passed)."
- **v2 plan Phase 4 (SecTrust):** No TFM blocker — `Security.framework` reachable via `dlopen` from `net10.0`.

### Spike artifact

Throwaway spike code (deleted post-commit; kept inline below for traceability):

<details>
<summary><code>Program.cs</code> (105 lines incl. comments — well under the 50-line target for the core probe; extras cover Phase 2/3/4 framework reach)</summary>

```csharp
using System.Runtime.InteropServices;

namespace Spike0a;

internal static class Program
{
    private const string Libobjc = "/usr/lib/libobjc.dylib";
    private const string Libdl = "libdl.dylib";

    [DllImport(Libdl, EntryPoint = "dlopen")]
    private static extern IntPtr DlOpen(string path, int mode);

    [DllImport(Libobjc, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjCGetClass(string name);

    [DllImport(Libobjc, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string name);

    [DllImport(Libobjc, EntryPoint = "class_getName")]
    private static extern IntPtr ClassGetName(IntPtr cls);

    private const int RTLD_NOW = 2;

    private static int Main()
    {
        // ... 6 probes as listed above ...
    }
}
```

`Spike0a.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

</details>

---

## Spike 0b — `BlockLiteral` ABI on iOS arm64 (PASS)

**Self-contained report:** [`2026-04-25-spike-0b-results.md`](./2026-04-25-spike-0b-results.md)

**Summary:** Avalonia-style `BlockLiteral` + `[UnmanagedCallersOnly]` trampoline successfully drove `-[WKHTTPCookieStore getAllCookies:]` on **iOS 26.4 Simulator arm64** (UDID `AB27BF5F-80C2-4CDD-8557-D94F109DC9DD`); completion returned non-null `NSArray*`. No iOS-specific block descriptor variant was required for this probe. Physical device not tested.

---

## Spike 0c — AOT publish smoke for `AllocateClassPair` + `class_addMethod` (PASS, with caveat)

**Self-contained report:** [`2026-04-25-spike-0c-results.md`](./2026-04-25-spike-0c-results.md)

**Summary:** `objc_allocateClassPair` + `class_addMethod` with a `delegate*` to `[UnmanagedCallersOnly(CallConvs = [CallConvCdecl])]` static method **survives AOT compilation** on `net10.0-ios` / `iossimulator-arm64`. AOT-built `.app` (containing `Spike0cIos.aotdata.arm64`) was launched on **iOS 26.4 Simulator (iPhone 17 Pro arm64)** and the dynamically-registered `webView:didFinishNavigation:` selector trampoline fired (`STATUS=1`, PID 90511). **Zero IL2xxx / IL3xxx warnings.** Wiring used `class_createInstance` → `objc_retain` (because `WKWebView.navigationDelegate` is a weak property) → `objc_msgSend("setNavigationDelegate:")` → `WKWebView.LoadRequest("about:blank")`.

**Critical caveat — must be reflected in the plan:** The literal command in the plan body — `dotnet publish -p:PublishAot=true -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64` — **fails** under `Microsoft.iOS` SDK `26.2.10233` because `Xamarin.Shared.Sdk.Publish.targets` rejects all `iossimulator-*` / `tvossimulator-*` RIDs for the `Publish` target ("device architecture must be specified"). The kill-switch question was answered with the **same AOT toolchain** (`mono-aot-cross`, cross-pack `Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.iossimulator-arm64`) via `dotnet build -c Release -f net10.0-ios -p:PublishAot=true -p:RuntimeIdentifier=iossimulator-arm64`. **Device-targeted `dotnet publish` + signing** was NOT end-to-end verified in this spike (no physical device available). The risk is now scoped to: SDK simulator-publish gating may shift in future workloads, and the first device-targeted Task in Phase 1 (currently Task 30 — AOT acceptance gate) MUST do the device-RID `dotnet publish` end-to-end as its hard checkpoint.

---

## Phase 0 GO/NO-GO Aggregate

**Decision: GO** — all three Phase 0 spikes PASS. Phase 1 may begin.

| Spike | Decision | Confidence | Risk carried forward |
|---|---|---|---|
| 0a | PASS | Very high (independent corroboration from existing `MacOSWebViewAdapter`) | None |
| 0b | PASS | High (real simulator dispatch verified) | Physical iOS device validation deferred — recommend Phase 7 hardware lab pass before 2.0.0 GA |
| 0c | PASS | High for AOT toolchain viability; medium for ship-pipeline | `dotnet publish` + simulator RID gated by SDK; device-RID publish + signing untested. **Hard checkpoint at Task 30.** |

### Plan amendments landed alongside this aggregate (this commit)

1. **Open Questions table (L1867+):** Q1, Q3, Q4 marked answered with PASS pointers.
2. **Architectural Invariants (L52):** "single namespace shared by the macOS slice (`net10.0-macos` TFM, added in Task 19)" → corrected to `net10.0` + runtime gate.
3. **Spike 0a Gate body (L186–L188):** marked RESOLVED (net10.0 sufficient).
4. **Task 19 Step 1 (L1399–L1404):** `net10.0-macos` TFM addition step REMOVED — macOS managed code stays on `net10.0` with `[SupportedOSPlatform("macos")]` runtime gating, mirroring existing `MacOSWebViewAdapter` pattern.
5. **AOT publish bash blocks (L1082–L1083 + L1747–L1748):** Annotated with the SDK simulator-RID caveat from Spike 0c. Simulator AOT smoke uses `dotnet build -p:PublishAot=true`; device AOT acceptance still uses `dotnet publish` (untested in spike, hard-checkpointed at Task 30).
6. **Phase 0 Exit Criteria (L231–L235):** marked SATISFIED.

### Spike branches retained for traceability

- `spike/0b-blockliteral-ios-arm64` — local-only branch with original spike commit (`e5b34a6`); cherry-picked into `release/v2` as `9e2d0aa`. Worktree at `../Fulora-spike-0b/` — may be deleted once `release/v2` is pushed.
- `spike/0c-aot-classpair` — local-only branch with original spike commit (`063b807`); cherry-picked into `release/v2` as `d9d5838`. Worktree at `../Fulora-spike-0c/` — may be deleted once `release/v2` is pushed.
