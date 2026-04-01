# Architecture

## The Problem Fulora Solves

Most hybrid frameworks treat the WebView as a black box — you load a URL and hope for the best. When your frontend needs to read a file, query a database, or call an AI model, you're back to building HTTP APIs, managing ports, and serializing everything by hand.

Fulora takes a different approach: the web frontend and the native host share a **typed contract layer** that is enforced at compile time. The runtime handles serialization, routing, lifecycle, and error propagation — your code on both sides just calls methods and gets typed results back.

## System Topology

```
┌──────────────────────────────────────────────────────────┐
│                   Your Application                       │
│      Avalonia UI + Web Frontend + Bridge Contracts       │
└──────────────────────────────┬───────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────┐
│                       Runtime Core                       │
│  Typed Bridge · Capability Gateway · Policy Engine       │
│  Diagnostics Pipeline · Shell Experience · SPA Hosting   │
└──────────────────────────────┬───────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────┐
│                 Adapter Abstraction Layer                 │
│                  IWebViewAdapter + Facets                 │
└──────────────────────────────┬───────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────┐
│   WebView2 (Win) · WKWebView (macOS/iOS) · Android WV   │
│                    WebKitGTK (Linux)                      │
└──────────────────────────────────────────────────────────┘
```

The key insight: **your application code never touches the WebView engine directly**. The runtime core mediates everything — bridge calls, capability requests, SPA hosting, diagnostics — through well-defined abstractions. This enables one runtime model to be reused across five host platforms while capability/support outcomes remain tiered and governed by the registry and platform status snapshots.

## Four-Layer Runtime Model

Fulora's product-grade architecture governance follows a four-layer model:

1. **Kernel** — runtime invariants, contract semantics, and platform-agnostic lifecycle rules.
2. **Bridge** — typed C# ↔ JavaScript contract surface and generated marshalling.
3. **Framework Services** — policy, diagnostics, shell, hosting, and other governed services.
4. **Plugins / Vertical Features** — domain-specific extensions that compose on top of stable layers.

Dependency direction is top-down only: higher layers can depend on lower layers, and lower layers must not depend on higher layers.

## Bridge Model

The bridge is the heart of Fulora. It turns C# interfaces into callable services on both sides of the boundary:

- **`[JsExport]`** — a C# interface whose implementation lives in the native host, callable from JavaScript
- **`[JsImport]`** — a C# interface whose implementation lives in JavaScript, callable from C#

A Roslyn source generator produces all marshalling code at compile time. There is no reflection, no dynamic proxies, no runtime code generation — which means it works with Native AOT out of the box.

Under the hood, calls travel as JSON-RPC messages over the WebView's message channel. But you never see this — you write `await GreeterService.greet("World")` in JavaScript and get a typed result back.

### Streaming

Methods returning `IAsyncEnumerable<T>` in C# become `AsyncIterable` in JavaScript. This powers scenarios like AI token streaming, live sensor data, and file content streaming — with proper backpressure and cancellation (`AbortSignal` maps to `CancellationToken`).

## Capability Gateway

Not every bridge call is a simple request-response. Some operations — file access, network requests, system notifications — need governance. The capability gateway provides this:

1. A capability request enters through the typed gateway
2. The **policy engine** evaluates authorization rules before any execution happens
3. The provider executes only when policy permits
4. A deterministic result is returned: `allow`, `deny`, or `failure`
5. A diagnostics event is emitted for observability

This means your app's security model is declarative and auditable, not scattered across `if` checks.

## Shell Experience

The `IWindowShellService` gives the web frontend control over native window properties:

- **Theme** — light, dark, or follow system
- **Transparency** — Mica (Windows 11), Acrylic, Blur, or None
- **Custom chrome** — drag regions, interactive exclusion zones
- **State streaming** — `streamWindowShellState()` pushes theme changes, transparency changes, and chrome metrics to the frontend in real time

## Deep-Link Architecture

For apps that register OS protocol handlers:

```
OS Protocol Handler → DeepLinkPlatformEntrypoint
    → DeepLinkRegistrationService (normalize → policy → idempotency)
        → WebViewShellActivationCoordinator (primary/secondary dispatch)
            → Your handler
```

Secondary instances forward activation to the primary. Duplicate activations are suppressed via idempotency keys. Every lifecycle stage emits structured diagnostics.

## Security Layers

- **Origin policy** — only declared origins can send bridge messages
- **Capability policy** — explicit evaluation before host operations execute
- **Rate limiting** — bounded request pressure on bridge and capability paths
- **Explicit exposure** — only registered contracts are reachable from web content

## Testability

- **Contract tests** validate bridge behavior independent of any platform WebView engine
- **Integration tests** validate runtime wiring on real platform adapters
- **Automation lanes** validate governance and diagnostics expectations for release readiness
- **`MockBridgeService`** enables unit testing without a real browser

## Related Documents

- [Getting Started](./getting-started.md) — Build your first app
- [Bridge Guide](./bridge-guide.md) — Advanced bridge patterns
- [Architecture Layering](../architecture-layering.md) — Four-layer boundaries and dependency rules
- [Product Platform Roadmap](../product-platform-roadmap.md) — Platform direction and execution phases
- [Platform Status](../platform-status.md) — Current governed capability/support snapshot
