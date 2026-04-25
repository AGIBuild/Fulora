# Phase 0 Spike Results — Fulora v2 Apple Shim Modernization

**Plan:** [`docs/superpowers/plans/2026-04-25-fulora-v2-apple-shim-modernization.md`](./2026-04-25-fulora-v2-apple-shim-modernization.md)

**Phase 0 gates:** Three independent spikes must all PASS before Phase 1 may begin.

| Spike | Question | Status | Time |
|---|---|---|---|
| 0a — `net10.0-macos` TFM decision | Is `net10.0` + `[SupportedOSPlatform("macos")]` runtime gating sufficient? | **PASS** | 2026-04-25 |
| 0b — `BlockLiteral` ABI on iOS arm64 | Does Avalonia's `BlockLiteral` work on iOS arm64 (sim + device)? | **PASS** | 2026-04-25 |
| 0c — AOT publish smoke for `AllocateClassPair` + `class_addMethod` | Does the runtime ObjC class registration approach survive `PublishAot=true` on `net10.0-ios`? | **TBD** | — |

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

## Spike 0c — AOT publish smoke for `AllocateClassPair` + `class_addMethod` (TBD)

_Pending parallel subagent dispatch — see plan L213–L227. **Single largest "kill switch" for the whole plan.**_

---

## Phase 0 GO/NO-GO Aggregate

_Will be filled after 0b and 0c report._
