# Phase 0 Spike 0b — `BlockLiteral` ABI on iOS arm64 (results)

**Plan:** [`2026-04-25-fulora-v2-apple-shim-modernization.md`](./2026-04-25-fulora-v2-apple-shim-modernization.md) (L196–L209)

**Date:** 2026-04-25 · **Host:** macOS 26.4.1 · **SDK:** .NET 10.0.201 / iOS workload 26.2.10233 · **Simulator arch:** `iossimulator-arm64`

---

## Method

1. **Vendored `BlockLiteral` pattern from Avalonia.Controls.WebView** (MIT), not the full Microsoft.iOS `ObjCRuntime.BlockLiteral` machinery.
   - Upstream file: [`BlockLiteral.cs`](https://github.com/AvaloniaUI/Avalonia.Controls.WebView/blob/4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e/src/Avalonia.Controls.WebView.Core/Macios/Interop/BlockLiteral.cs)
   - Commit SHA: `4e16564d5c0d1c6b4ccc0ab35f69be75fe673a2e`
   - Added companion `Libobjc.cs` with `_Block_copy`, `dlopen`, `dlsym` using **`/usr/lib/libobjc.dylib`** and **`/usr/lib/libSystem.dylib`** (same paths as [dotnet/macios `Constants.cs`](https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Constants.cs)).
   - **Spike-only tweak:** `Block_descriptor` helper fields are `IntPtr` (nil) instead of `delegate*` function-pointer fields, preserving layout width on 64-bit.
2. Built minimal **`net10.0-ios`** app at `spikes/0b-iOS-app/`:
   - Obtained `WKHTTPCookieStore` from `WKWebsiteDataStore.DefaultDataStore.HttpCookieStore`.
   - Invoked **`-getAllCookies:`** via raw `objc_msgSend` (`DllImport`), passing a heap block from **`BlockLiteral.GetBlockForFunctionPointer`** + **`[UnmanagedCallersOnly]`** trampoline with signature `(IntPtr block, IntPtr nsArray)` — **not** Microsoft.iOS delegate-to-block marshalling for the completion handler.
   - Main-thread run loop pumped with `NSRunLoop.Main.RunUntil` until callback or 30s timeout.
3. **Simulator:** Booted **iPhone 17 Pro**, UDID **`AB27BF5F-80C2-4CDD-8557-D94F109DC9DD`**, runtime **`com.apple.CoreSimulator.SimRuntime.iOS-26-4`** (iOS 26.4 simulator).
4. **Physical device:** Not available in this environment (simulator-only).

---

## Probes

| # | Probe |
|---|--------|
| 1 | `_NSConcreteGlobalBlock` / `_Block_copy` resolve via `dlsym` + `libSystem` on iOS simulator |
| 2 | Vendored `BlockLiteral` produces non-null block pointer |
| 3 | `objc_msgSend` delivers block to `-[WKHTTPCookieStore getAllCookies:]` |
| 4 | Trampoline runs on callback thread; `NSArray*` argument **non-null** (`STATUS==1`) |

---

## Result

**Build:** `dotnet build -c Debug -f net10.0-ios` — **succeeded**, target `iossimulator-arm64`, **0 warnings, 0 errors** (Debug; no `IL2xxx` / `IL3xxx` in this configuration).

**Run:** `dotnet build -c Debug -f net10.0-ios -t:Run -p:_DeviceName=:v2:udid=AB27BF5F-80C2-4CDD-8557-D94F109DC9DD`

Console (simulator):

```
xcrun simctl launch --console --terminate-running-process AB27BF5F-80C2-4CDD-8557-D94F109DC9DD com.fulora.spike0b.blockliteral
com.fulora.spike0b.blockliteral: 80564
2026-04-25 13:52:14.084423+0800 Spike0bIos[80564:66624156] Microsoft.iOS: Socket error while connecting to IDE on 127.0.0.1:10000: Connection refused
2026-04-25 13:52:14.363377+0800 Spike0bIos[80564:66624114] [Spike0b] STATUS=1 (1=ok)
```

- **`STATUS=1`:** completion ran with **non-null** `NSArray*` (empty vs non-empty not distinguished; only pointer nullability checked).
- IDE socket message is benign without VS/Cursor debugger attached.

---

## Decision: PASS

**Rationale:** The Avalonia-style `BlockLiteral` + global block ISA + `_Block_copy` path **dispatches correctly** on **iOS 26.4 Simulator arm64** for a real WebKit async block API. No separate **iOS arm64 block-descriptor variant** was required for this probe.

**Caveat:** **Physical iOS arm64 device** not exercised; gate text allowed simulator-only when no device is available.

---

## Plan amendments required

- **None** for Phase 1 `BlockLiteral` vendoring based on descriptor layout — this spike did **not** find an iOS-specific layout divergence for the tested block shape.
- **Optional follow-up (non-blocking):** one smoke run on a physical device when hardware is available.

---

## Spike artifact (throwaway)

| Path | Purpose |
|------|---------|
| `spikes/0b-iOS-app/Spike0bIos.csproj` | `net10.0-ios` app |
| `spikes/0b-iOS-app/Interop/BlockLiteral.cs` | Vendored block layout + helpers (attribution header) |
| `spikes/0b-iOS-app/Interop/Libobjc.cs` | `_Block_copy` / `dlopen` / `dlsym` |
| `spikes/0b-iOS-app/CookieBlockProbe.cs` | `getAllCookies:` + `UnmanagedCallersOnly` trampoline |
| `spikes/0b-iOS-app/*.cs`, `Info.plist`, storyboard, assets | Template shell |

---

## iOS vs macOS arm64 layout (spike question)

For **stack/global `Block_literal` + minimal `Block_descriptor`** as used here, **observed behavior on iOS Simulator arm64 matches expectations from the same 64-bit Apple Block ABI** used on macOS arm64. No evidence in this spike that **`Block_descriptor` or `Block_literal` field order/sizes differ** between macOS arm64 and iOS arm64 for this pattern.

---

## Microsoft.iOS `BlockLiteral` note

The workload already ships **`ObjCRuntime.BlockLiteral`** (see dotnet/macios [`Blocks.cs`](https://github.com/dotnet/macios/blob/main/src/ObjCRuntime/Blocks.cs)). This spike **intentionally used a vendored Avalonia-style block** to answer the plan’s Avalonia path. A separate codebase could instead rely on Microsoft.iOS’s type for managed block interop where full registrar/runtime integration is acceptable.
