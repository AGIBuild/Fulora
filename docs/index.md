# Fulora

**Ship web apps as native desktop products** — with real OS integration, not just a wrapper.

Fulora lets you write your UI in React, Vue, or any web framework, then deliver it inside an Avalonia-powered native window with full access to file systems, system APIs, AI models, and more — all through a type-safe C# ↔ JavaScript bridge that generates code at compile time.

> One runtime core. Five platforms. Zero reflection.

---

## Why Fulora?

Most hybrid frameworks give you a browser in a box. Fulora gives you a **product-grade application shell** where the web frontend and the native host are equal partners:

- Your React component calls `AiChatService.streamCompletion(prompt)` and gets back an `AsyncIterable` of tokens — streamed from a local Ollama model running on the user's machine.
- Your settings page calls `WindowShellService.updateWindowShellSettings({ theme: 'dark', enableTransparency: true })` and the native window responds instantly with Mica/Acrylic effects.
- Your file browser calls `FileService.listFiles(path)` and navigates the real file system — something browsers will never allow.

All of this is type-checked at compile time and AOT-safe, with one runtime model spanning Windows, macOS, Linux, iOS, and Android. Platform capability support is tiered and governed through the capability registry and platform status snapshots.

---

## Start Building

| What you want to do | Where to go |
|---|---|
| Build your first hybrid app in 5 minutes | [Getting Started](articles/getting-started.md) |
| See a full working sample | [Demo: Avalonia + React Hybrid App](demo/index.md) |
| Build an AI chat app with streaming | [AI Integration Guide](ai-integration-guide.md) |
| Understand how the runtime works | [Architecture](articles/architecture.md) |

## Platform Documents

> Capability support truth source: treat tier + registry + status as the governed source of truth. The five-platform product positioning is directional and does not mean every capability is already fully covered across all five platforms in the current registry snapshot.

| Platform document | Purpose |
|---|---|
| [Product Platform Roadmap](product-platform-roadmap.md) | Positioning, strategy, stable core boundaries, capability contract, and P0-P5 execution roadmap |
| [Architecture Layering](architecture-layering.md) | Four-layer model, allowed dependencies, API types, decision tree, and kernel approval rules |
| [Platform Status](platform-status.md) | Current release-line placeholder for Tier A/B/C support and known limitations |
| [Release Governance](release-governance.md) | Stable release rules, release gates, package smoke, and promotion flow |
| [Framework Capabilities](framework-capabilities.json) | Starter machine-readable capability registry with tier, support, contract, and limitations metadata |

## Developer Resources

| Resource | Description |
|---|---|
| [Bridge Guide](articles/bridge-guide.md) | `[JsExport]`, `[JsImport]`, streaming, cancellation, error handling |
| [SPA Hosting](articles/spa-hosting.md) | Embedded resources, `app://` scheme, dev server proxy, HMR |
| [CLI Reference](cli.md) | `fulora new`, `dev`, `generate types`, `add service` |
| [Plugin Authoring](plugin-authoring-guide.md) | Create bridge plugins that ship as NuGet + npm packages |
| [Bridge DevTools](bridge-devtools-panel.md) | In-app debug overlay for bridge call inspection |
| [Native AOT](nativeaot.md) | Trim-safe publishing and AOT compilation guidance |
| [E2E Testing](hybrid-e2e-testing-guide.md) | End-to-end testing patterns for hybrid apps |
| [Shipping Your App](shipping-your-app.md) | Packaging, distribution, and auto-update strategies |

## Operations

| Resource | Description |
|---|---|
| [Release Checklist](release-checklist.md) | How to publish a new release via the unified CI/Release pipeline |
| [Documentation Deployment](docs-site-deploy.md) | How the docs site is built and deployed |

## What's Inside

- **Type-Safe Bridge** — `[JsExport]` / `[JsImport]` attributes with Roslyn source generation. No reflection, no runtime proxies, fully AOT-compatible.
- **Streaming** — `IAsyncEnumerable<T>` on C# maps to `AsyncIterable` in JavaScript. Stream AI tokens, file contents, or sensor data with backpressure and cancellation.
- **5 Platforms Direction** — Windows (WebView2), macOS/iOS (WKWebView), Android (WebView), Linux (WebKitGTK). Fulora targets one runtime model across these hosts; governed capability support remains tiered and documented in registry/status.
- **Plugin Ecosystem** — Database (SQLite), HTTP Client, File System, Notifications, Auth Token, Local Storage — ship as paired NuGet + npm packages.
- **Window Shell** — Theme control, transparency, custom chrome, drag regions — all driven from the web frontend.
- **OpenTelemetry** — Bridge call spans and metrics export to any OTLP backend. See exactly what crosses the bridge and how long it takes.
- **HMR Preservation** — Bridge state survives hot module replacement. No more losing connections during frontend development.

## Roadmap

Roadmap phases are directional planning context, not the governed release fact source.
For externally communicated support/status, use Platform Status and Release Governance.

[Product Platform Roadmap](product-platform-roadmap.md) · [Platform Status](platform-status.md)
