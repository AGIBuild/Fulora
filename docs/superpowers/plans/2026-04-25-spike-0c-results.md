# Phase 0 Spike 0c — AOT + `AllocateClassPair` / `class_addMethod` on iOS (results)

**Plan:** [`2026-04-25-fulora-v2-apple-shim-modernization.md`](./2026-04-25-fulora-v2-apple-shim-modernization.md) (L213–L227)

**Date:** 2026-04-25 · **Host:** macOS 26.4.1 · **SDK:** .NET 10.0.201 / iOS workload **26.2.10233** · **Simulator:** `iossimulator-arm64` · **Simulator runtime:** iOS 26.4 (`com.apple.CoreSimulator.SimRuntime.iOS-26-4`)

---

## Method

1. **Template:** Copied `spikes/0b-iOS-app/` → `spikes/0c-iOS-app/` (Info.plist, storyboard, assets, entitlements). **Did not modify 0b.**
2. **Interop:** Reused vendored `Interop/BlockLiteral.cs` (namespace `Spike0c.Interop`). Extended `Interop/Libobjc.cs` with dynamic registration entry points: `objc_lookUpClass`, `objc_allocateClassPair`, `objc_registerClassPair`, `sel_registerName`, `class_addMethod`, `class_createInstance`, `objc_retain` (plus existing `_Block_copy` / `dlopen` / `dlsym`).
3. **Runtime class:** Single dynamic NSObject subclass `FuloraSpikeNavDelegate0c`. On first use: `objc_allocateClassPair` → `class_addMethod` for `webView:didFinishNavigation:` with types **`v@:@@`** and IMP = address of **`[UnmanagedCallersOnly(CallConvs = [CallConvCdecl])]`** static `DidFinishNavigationTrampoline(self, _cmd, webView, navigation)` → `objc_registerClassPair`. Subsequent runs use `objc_lookUpClass`.
4. **Wiring:** `class_createInstance` → **`objc_retain`** (property `navigationDelegate` is **weak**) → **`objc_msgSend`** `setNavigationDelegate:` with the dynamic instance → `WKWebView.LoadRequest(NSUrl.FromString("about:blank"))` → main run loop until trampoline sets status or 30s timeout.
5. **AOT probe:** `dotnet publish` with `-p:PublishAot=true -p:RuntimeIdentifier=iossimulator-arm64` **fails** — Microsoft.iOS SDK `Xamarin.Shared.Sdk.Publish.targets` **rejects all `iossimulator-*` / `tvossimulator-*` RIDs** for publish (“device architecture must be specified”). **Actual AOT smoke:**  
   `dotnet build -c Release -f net10.0-ios -p:PublishAot=true -p:RuntimeIdentifier=iossimulator-arm64`  
   which invokes **`mono-aot-cross`** (cross-pack `Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.iossimulator-arm64`) and places **`Spike0cIos.aotdata.arm64`** (and peers) inside **`Spike0cIos.app`**.

### Class registration excerpt (Spike 0c)

```csharp
IntPtr cls = Libobjc.objc_lookUpClass(DynamicClassName);
if (cls == IntPtr.Zero) {
    cls = Libobjc.objc_allocateClassPair(super, DynamicClassName, 0);
    IntPtr selNav = Libobjc.sel_registerName("webView:didFinishNavigation:");
    unsafe {
        delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> imp = &DidFinishNavigationTrampoline;
        byte ok = Libobjc.class_addMethod(cls, selNav, (IntPtr)(void*)imp, "v@:@@");
        if (ok == 0) throw new InvalidOperationException("class_addMethod returned false");
    }
    Libobjc.objc_registerClassPair(cls);
}
IntPtr del = Libobjc.class_createInstance(cls, 0);
_ = Libobjc.objc_retain(del);
objc_msgSend_id(webView.Handle, new Selector("setNavigationDelegate:").Handle, del);
```

---

## Probes

| # | Probe | Outcome |
|---|--------|---------|
| 1 | `dotnet publish` … `iossimulator-arm64` + `PublishAot=true` | **Fails** at `_PrePublish` (simulator RID disallowed for `Publish` target) |
| 2 | `dotnet build` … `iossimulator-arm64` + `PublishAot=true` | **Succeeds**; AOT inputs/cache + `.aotdata.arm64` in `.app` |
| 3 | `xcrun simctl install` + `launch --console` | **Succeeds** |
| 4 | `webView:didFinishNavigation:` trampoline sets `STATUS=1` | **Observed** |

---

## Result

**Publish (simulator):**

```text
/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.2/26.2.10233/targets/Xamarin.Shared.Sdk.Publish.targets(14,3): error : A runtime identifier for a device architecture must be specified in order to publish this project. 'iossimulator-arm64' is a simulator architecture.
```

Exit code: **1** (expected SDK guard; not an IL/AOT compiler failure).

**AOT build (simulator):**

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Simulator run** (booted **iPhone 17 Pro**, UDID **`AB27BF5F-80C2-4CDD-8557-D94F109DC9DD`**):

```text
2026-04-25 14:02:57.172 Spike0cIos[90511:66674428] [Spike0c] STATUS=1 (1=didFinishNavigation fired)
```

- **Bundle ID:** `com.fulora.spike0c.aotnav`
- **Process:** `Spike0cIos` **pid 90511** (from log line above)

**Optional:** `dotnet publish` with **`ios-arm64`** + `PublishAot=true` progressed past RID check but **failed** on this machine with **no code-signing key** (environment limitation; not evaluated as architectural NO_GO).

---

## AOT warnings encountered

| Code | Location | Decision |
|------|----------|----------|
| *(none)* | — | Release AOT build reported **0** compiler warnings; no `IL2xxx` / `IL3xxx` surfaced in the build log for this minimal app. |

---

## Decision: PASS

**Rationale:** With **`PublishAot=true`**, the **`AllocateClassPair` + `class_addMethod` + `delegate*` + `[UnmanagedCallersOnly]`** path **builds** for **`net10.0-ios` / `iossimulator-arm64`**, produces **AOT images** in the app bundle, and the **published build output** (`Spike0cIos.app` from `dotnet build`) **runs on the simulator** with **`webView:didFinishNavigation:`** dispatching into the managed static trampoline (**`STATUS=1`**).

**Caveat:** **`dotnet publish` cannot target simulator RIDs** in the current Microsoft.iOS SDK; the spike used **`dotnet build`** as the AOT packaging step for simulator. **Physical device** and **`dotnet publish` + `ios-arm64`** were not fully validated here (signing).

---

## Plan amendments required

1. **Spike 0c procedure text:** For simulator, document **`dotnet build -c Release -f net10.0-ios -p:PublishAot=true -p:RuntimeIdentifier=iossimulator-arm64`** (or chosen sim RID) as the AOT smoke when **`dotnet publish` is blocked** for `iossimulator-*`.
2. **Optional:** Note that **device publish** may require Apple code signing even for spike automation.

---

## Spike artifact

| Path | Purpose |
|------|---------|
| `spikes/0c-iOS-app/Spike0cIos.csproj` | `net10.0-ios` app; `TrimmerSingleWarn=false`, `EnableAotAnalyzer=true` |
| `spikes/0c-iOS-app/Interop/BlockLiteral.cs` | Vendored from 0b (namespace `Spike0c.Interop`) |
| `spikes/0c-iOS-app/Interop/Libobjc.cs` | Block + dynamic class registration P/Invoke |
| `spikes/0c-iOS-app/NavDelegateProbe.cs` | Dynamic nav delegate + `objc_msgSend` wiring |
| `spikes/0c-iOS-app/*.cs`, `Info.plist`, storyboard, assets | App shell |

---

## Microsoft.iOS `dotnet publish` note

`Xamarin.Shared.Sdk.Publish.targets` enforces **device-only** RIDs for the **Publish** target (lines 13–21 in pack **26.2.10233**). This is **orthogonal** to whether **`class_addMethod` IMPs** survive AOT; the spike still answers the architectural question via **AOT-enabled build output** executed on the simulator.
