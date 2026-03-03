# Fulora

Web-first product velocity with Avalonia-native performance, control, and security.

## Documentation Hub

This project targets a **framework-grade C# + web development model** while preserving **standalone WebView control integration flexibility**.
One runtime core supports both paths.

Use this page as the entry point based on what you want to do next.

## Start Here

- **Build your first app**: [Getting Started](articles/getting-started.md)
- **Understand architecture decisions**: [Architecture](articles/architecture.md)
- **See a real sample experience**: [Demo: Avalonia + React Hybrid App](demo/index.md)
- **Check product direction and phases**: [Roadmap](../openspec/ROADMAP.md)
- **Review goals and positioning**: [Project Vision & Goals](../openspec/PROJECT.md)

## Developer Resources

- **[CLI Reference](cli.md)** — `fulora new`, `dev`, `generate types`, `add service`
- **[Bridge DevTools Panel](bridge-devtools-panel.md)** — In-app debug overlay for bridge call inspection
- **[Plugin Authoring Guide](plugin-authoring-guide.md)** — Create and publish bridge plugins (NuGet + npm)
- **[Documentation Site Deployment](docs-site-deploy.md)** — How the docs site is built and deployed
- **[Release Checklist](release-checklist.md)** — Steps for publishing a new release

## Features

- **Type-Safe Bridge**: `[JsExport]` / `[JsImport]` attributes with Roslyn Source Generator for AOT-compatible C# ↔ JS interop
- **SPA Hosting**: Embedded resource serving with custom `app://` scheme, SPA router fallback, dev server proxy
- **Cross-Platform**: Windows (WebView2), macOS/iOS (WKWebView), Android (WebView), Linux (WebKitGTK)
- **Testable**: `MockBridgeService` for unit testing without a real browser
- **Secure**: Origin-based policy, rate limiting, protocol versioning
- **Plugin Ecosystem**: Official plugins for Database (SQLite), HTTP Client, File System, Notifications, Auth Token
- **OpenTelemetry**: Bridge call spans and metrics export to any OTLP backend
- **IDE Tooling**: VS Code extension with live bridge call visualization
- **Bridge Profiler**: Per-service/method P50/P95/P99 latency statistics
- **Web Worker Bridge**: Type-safe bridge calls from Web Workers via MessagePort relay
- **HMR Preservation**: Bridge state preserved across hot module replacement reloads
- **Enhanced Diagnostics**: Rich error codes with actionable hints for bridge call failures

## Current Product Objective

**Phase 11 (Ecosystem & Developer Experience)** is complete. The framework now includes:

- 5 official bridge plugins (Database, HTTP Client, File System, Notifications, Auth Token)
- Plugin registry discovery via `fulora search/add/list` CLI commands
- OpenTelemetry provider package (`Agibuild.Fulora.Telemetry.OpenTelemetry`)
- VS Code bridge extension with WebSocket debug protocol
- Bridge call profiler with statistical aggregation
- Web Worker bridge support
- Full-featured showcase Todo app and interactive playground
- Tag-driven release automation pipeline for NuGet + npm

**Next**: Phase 12 — Enterprise & Advanced Scenarios (Planned)

## Roadmap Snapshot

| Phase | Focus | Status |
|---|---|---|
| Phase 0–3 | Foundation, Bridge, SPA, Polish | ✅ Done |
| Phase 4 | Application Shell | ✅ Done |
| Phase 5 | Framework Positioning | ✅ Done |
| Phase 6 | Governance Productization | ✅ Done |
| Phase 7 | Release Orchestration | ✅ Done |
| Phase 8 | Bridge V2 & Platform Parity | ✅ Done |
| Phase 9 | GA Release (1.0.0) | ✅ Done |
| Phase 10 | Production Operations & Ecosystem | ✅ Done |
| Phase 11 | Ecosystem & Developer Experience | ✅ Done |
| Phase 12 | Enterprise & Advanced Scenarios | Planned |
