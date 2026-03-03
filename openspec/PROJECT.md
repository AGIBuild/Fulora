# Fulora — Project Vision & Goals

> This document defines **what we are building, why, and what success looks like**.
> All OpenSpec changes, specs, and roadmap items should align with the goals stated here.

---

## 1. Project Identity

**Fulora** is a cross-platform hybrid application framework for [Avalonia UI](https://avaloniaui.net/) that enables developers to build desktop and mobile applications combining native C# UI with web-based UI — with type-safe communication, native performance, and security by default.

**One-liner**: _Web-first developer speed with native-grade control, performance, and trust._

**Adoption model**: one runtime core, two usage paths — start as a standalone `WebView` control in custom architectures, or adopt the full framework stack for end-to-end hybrid product delivery.

**Strategic promise**: teams should be able to move from "embed a page" to "ship a full cross-platform product" on the same architecture without rewrites.

---

## 2. Problem Statement

### 2.1 Lessons from Current Web-First Desktop Approaches

Modern bundled-browser desktop stacks offer unmatched developer productivity — the entire npm/frontend ecosystem, hot reload, and rapid UI iteration. But they come with fundamental trade-offs:

| Pain Point | Impact |
|---|---|
| **App size 200MB+** | Every app bundles its own Chromium; users install the same engine hundreds of times |
| **Memory 150MB+/window** | Each app process runs a full browser engine |
| **Cold start 3-5 seconds** | Chromium initialization is inherently slow |
| **No native look & feel** | Everything is web — menus, dialogs, notifications all feel "off" |
| **Security complexity** | `nodeIntegration` / `contextIsolation` / `preload` — easy to misconfigure, hard to audit |
| **Untyped IPC** | `ipcMain` / `ipcRenderer` are string-based, no compile-time safety, major bug source |
| **No native integration** | Cannot mix native OS controls with web content within the same window |

Teams tolerate these issues because **few alternatives offer the same mix of frontend velocity and ecosystem leverage**.

### 2.2 Why Traditional WebView Controls Still Fall Short

Existing Avalonia/WPF WebView controls (community packages, platform-specific wrappers) solve the size/memory problem by using the system WebView, but introduce different pain points:

| Pain Point | Impact |
|---|---|
| **Minimal API surface** | Basic navigation only — no cookies, no interception, no scripting bridge |
| **No cross-platform abstraction** | Different code per platform, inconsistent behavior |
| **WebView as a black box** | No structured communication between native and web layers |
| **Untestable** | Cannot unit test any WebView interaction without a real browser |
| **No security model** | No origin checks, no message policy, no capability gating |
| **No hybrid app story** | No SPA hosting, no typed bridge, no dev tooling integration |

These controls solve rendering, but stop short of being a product framework.

### 2.3 The Position We Build

```
Bundled Browser Stack  Our Position              Pure Native Avalonia
◄──────────────────────────●──────────────────────────────────►

Full Web UI            Hybrid (Native + Web)     Full Native UI
200MB+ / 150MB RAM     5-10MB / Low RAM          Minimal footprint
Weak native integration  Seamless mix              No web capability
Untyped IPC            Type-safe bridge           N/A
Complex security       Secure by default          N/A
Rich dev tooling       Native + Web tooling       Native tooling only
```

---

## 3. Goals

### 3.1 Core Goals (Must Achieve)

These are the defining capabilities that make the project worth using:

| ID | Goal | Description |
|----|------|-------------|
| **G1** | **Type-Safe Bidirectional Bridge** | C# ↔ JS communication via source-generated proxies. `[JsExport]` exports C# services to JS; `[JsImport]` imports JS services into C#. Full type safety, zero reflection, compile-time validation. |
| **G2** | **First-Class SPA Hosting** | Serve React/Vue/Svelte/Angular apps via custom protocol (`app://`). Dev mode with HMR proxy, production mode with embedded resources. 5 lines of code to host a frontend app. |
| **G3** | **Secure by Default** | Capability-based security model. Web content can only call explicitly exposed methods. Origin allowlisting, channel isolation, and rate limiting built into the bridge layer. |
| **G4** | **Contract-Driven Testability** | Every feature testable via MockAdapter without a real browser. Source-generated bridge proxies are also fully testable. |

### 3.2 Foundation Goals (Already Achieved)

These are prerequisites that we have already delivered:

| ID | Goal | Status |
|----|------|--------|
| **F1** | Cross-platform native WebView adapters (Windows/macOS/iOS/Android/Linux) | ✅ Done |
| **F2** | Full-control navigation with interception, cancellation, redirect correlation | ✅ Done |
| **F3** | WebMessage bridge with policy (origin, channel, protocol checks) | ✅ Done |
| **F4** | Rich feature set: cookies, commands, screenshots, PDF, zoom, find, preload, context menu, downloads, permissions, web resource interception | ✅ Done |
| **F5** | Adapter lifecycle events and typed platform handles | ✅ Done |
| **F6** | JSON-RPC 2.0 bidirectional RPC over WebMessage bridge | ✅ Done |
| **F7** | 1500+ unit tests + 200+ integration tests, 97%+ line coverage | ✅ Done |
| **F8** | WebDialog and WebAuthBroker | ✅ Done |

### 3.3 Experience Goals (Differentiators)

| ID | Goal | Description |
|----|------|-------------|
| **E1** | **Project Template** | `dotnet new agibuild-hybrid` creates a ready-to-run Avalonia + React/Vue project with typed bridge |
| **E2** | **Dev Tooling** | Built-in DevTools toggle, structured logging, bridge call tracing |
| **E3** | **Hot Reload Integration** | Web content HMR works seamlessly during development, bridge state preserved across reloads |

---

## 4. Non-Goals

| What | Why |
|------|-----|
| Become a browser-shell clone | We borrow proven workflow ideas, but optimize for .NET hybrid architecture and explicit contracts |
| Bundled browser engine | We use platform WebViews — this is a feature, not a limitation |
| Full Chromium API parity | We expose what is useful through our abstraction, not every browser API |
| Server-side rendering | We are a client-side framework |
| Web-only deployment | Our apps are native desktop/mobile applications |

---

## 5. Target Users

| Persona | Need | How We Help |
|---------|------|-------------|
| **.NET desktop developers** | Add web-based UI sections (dashboards, rich editors) to native apps | Embed WebView with type-safe bridge, no web expertise needed |
| **Web-first .NET product teams** | Keep modern frontend velocity without giving up C# architecture control | SPA hosting + typed bridge + policy/governance yields fast iteration with deterministic host behavior |
| **Cross-platform app teams** | Single codebase for Windows/macOS/Linux/iOS/Android | Unified API across 5 platforms with consistent behavior |
| **Enterprise/security-conscious** | Auditable, sandboxed web content integration | Capability-based security, origin allowlisting, no `nodeIntegration` equivalent |

---

## 6. Success Metrics

| Metric | Target |
|--------|--------|
| App size (hello-world hybrid) | < 15 MB (vs typical bundled-browser desktop stacks at 200MB+) |
| Memory usage (single WebView) | < 50 MB (vs typical bundled-browser desktop stacks at 150MB+) |
| Bridge call latency (C# → JS round-trip) | < 5 ms |
| Time to first render (SPA hosted) | < 1 second |
| Type-safe bridge coverage | 100% of exposed methods have TS types |
| Test coverage (runtime + bridge) | > 95% line coverage |
| Supported platforms | 5 (Windows, macOS, iOS, Android, Linux) |

---

## 7. Competitive Positioning

| Capability | Bundled-browser desktop stacks | Tauri | Existing WebView Controls | **Agibuild** |
|---|---|---|---|---|
| App size | ❌ Usually large | ✅ Small | ✅ Small | ✅ Small |
| Memory | ❌ Usually high | ✅ Low | ✅ Low | ✅ Low |
| Native UI mixing | ❌ None | ❌ None | ⚠️ Basic | ✅ Seamless |
| JS ↔ Host type safety | ❌ None | ⚠️ Partial | ❌ None | ✅ **Full (source-gen)** |
| SPA hosting | ✅ Built-in | ✅ Built-in | ❌ None | ✅ Built-in |
| Security model | ⚠️ Complex | ✅ Good | ❌ None | ✅ **Secure by default** |
| Testability | ❌ Hard | ❌ Hard | ❌ Impossible | ✅ **MockAdapter + typed mocks** |
| Mobile support | ⚠️ Varies | ✅ Partial | ⚠️ Partial | ✅ 5 platforms |
| .NET ecosystem | ❌ None | ❌ None | ✅ .NET | ✅ **.NET native** |

---

## 8. References

- [Compatibility Matrix](../docs/agibuild_webview_compatibility_matrix_proposal.md) — Platform support levels and acceptance criteria
- [Design Document](../docs/agibuild_webview_design_doc.md) — Architecture, contracts, and implementation design
- [Contract Semantics v1](./specs/webview-contract-semantics-v1/spec.md) — Navigation, threading, and event ordering semantics
- [Roadmap](./ROADMAP.md) — Phased delivery plan aligned with these goals
