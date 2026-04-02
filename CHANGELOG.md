# Changelog

All notable changes to **Agibuild.Fulora** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.0] — 2026-03-07

### Stabilization & Quality Hardening

#### Added
- **Adapter Shared Utilities** — Extracted `NavigationErrorFactory` and `AdapterCookieParser` from platform adapters into `Agibuild.Fulora.Adapters.Abstractions` to eliminate cross-adapter code duplication.
- **Mutation Testing Infrastructure** — Stryker.NET integration with Nuke build target and CI workflow for mutation-based test quality validation.
- **Quality Hardening** — Template and sample Avalonia version alignment to `12.0.0-preview1`, additional test coverage for edge cases and error paths.

#### Changed
- **Runtime Service Relocation** — Moved `GlobalShortcutService` and `ThemeService` from Avalonia UI layer to `Agibuild.Fulora.Runtime` for proper layering and mutation testing scope inclusion.

### Phase 12: Enterprise & Advanced Scenarios

#### Added
- **Sentry Crash Reporting** (`Agibuild.Fulora.Telemetry.Sentry`) — `ITelemetryProvider` and `IBridgeTracer` implementations for Sentry SDK. One-line crash reporting via `AddSentry()` DI extension. Bridge call breadcrumbs, exception capture with scope enrichment, configurable parameter capture. (27 tests)
- **Shared State Management** (`ISharedStateStore`) — Cross-WebView reactive key-value store with last-writer-wins conflict resolution. Typed `Get<T>`/`Set<T>`, `StateChanged` event notifications, immutable snapshots, DI registration via `AddSharedState()`. (21 tests)
- **OAuth PKCE Client** (`Agibuild.Fulora.Auth.OAuth`) — RFC 7636-compliant PKCE flow for enterprise SSO. `PkceHelper` for code verifier/challenge generation, `OAuthPkceClient` for authorization URL building, token exchange, and token refresh. (23 tests)
- **Plugin Quality & Compatibility** — `fulora-plugin.json` manifest in all 7 official plugins with machine-readable version compatibility, platform support, and service declarations. `PluginManifest` model in Core. CLI `fulora list plugins --check` for version compatibility validation. (14 tests)

---

## [1.0.0] — 2026-03-01

### Phase 9: GA Release Readiness

- **API Surface Freeze** — 172 public types (72 Core + 100 Runtime) audited and frozen for 1.0. All `[Experimental]` attributes explicitly resolved.
- **npm Bridge Publication** — `@agibuild/bridge` package metadata and `NpmPublish` nuke target for automated npm registry publication.
- **Performance Re-baseline** — Added CancellationToken dispatch and IAsyncEnumerable streaming benchmarks. Updated baseline with 5 metrics.
- **Changelog & Migration Guide** — Structured changelog and Electron → Fulora migration guide.
- **Stable Release Gate** — Version 1.0.0 GA with all governance targets passing.

---

## [0.1.21-preview] — 2026-03-01

### Phase 8: Bridge V2 & Platform Parity

#### Added
- **Bridge Diagnostics Safety Net** — Comprehensive bridge tracing and diagnostic event infrastructure.
- **CancellationToken Support** — `CancellationToken` parameter in `[JsExport]` service methods; maps to JS `AbortSignal`.
- **IAsyncEnumerable Streaming** — `IAsyncEnumerable<T>` return types in bridge services; maps to JS `AsyncIterable`.
- **Method Overloads & Generics** — Bridge source generator handles overloaded methods and generic type boundaries.
- **Binary Payload** — `byte[]` ↔ `Uint8Array` bridging for binary data transfer.
- **SPA Asset Hot Update** — Secure, atomic, signed package-based SPA asset updates with rollback support.
- **Shell Activation Orchestration** — Runtime component for managing app instance activations and deep-link dispatch.
- **Deep-Link Native Registration** — OS-level URI scheme registration with typed route declarations, policy admission, and idempotent ingestion.
- **Platform Feature Parity** — Cross-platform compatibility validation and adapter gap closure.

---

## [0.1.10-preview] — 2026-02-28

### Phase 7: Release Orchestration

#### Added
- **Continuous Transition Gate** — Machine-checkable phase transition markers in ROADMAP with governance enforcement.
- **Release Closeout Snapshot** — CI evidence artifacts for phase completion validation.
- **Distribution Readiness Governance** — Deterministic NuGet package validation before release.
- **Adoption Readiness Governance** — Template E2E, sample app, and documentation completeness gates.
- **Dependency Vulnerability Governance** — Automated `npm audit` and NuGet vulnerability scanning.

### Changed
- **Build Modularization** — Split monolithic Nuke build into responsibility-partitioned `BuildTask` partials.

---

## [0.1.9-preview] — 2026-02-27

### Phase 6: Foundation Governance Productization

#### Added
- **TypeScript Declaration Governance** — Validates `@agibuild/bridge` package structure and TypeScript declarations.
- **Bridge Distribution Governance** — npm/pnpm/yarn parity smoke tests and Node LTS import validation.
- **Legacy Spec Validation Governance** — strict spec validation was integrated into the CI pipeline during this phase.
- **Runtime Critical-Path Governance** — Shell production matrix and runtime manifest sync enforcement.

### Changed
- **Product Identity** — Rebranded from Agibuild.WebView to **Agibuild.Fulora** across all packages, namespaces, and documentation.
- **Architecture Decoupling** — Separated `Core`/`Runtime` from Avalonia host layer for framework-agnostic consumption.

---

## [0.1.5-preview] — 2026-02-25

### Phase 5: Framework Positioning

#### Added
- **Typed Capability Gateway** — Policy-first host capability execution model (clipboard, file dialogs, notifications, external open).
- **System Integration Event Flow** — Bidirectional native ↔ web event bridging with budget control and pruning audit.
- **Profile-Federated Integration** — Session and permission profiles with shell presets in project template.
- **Agent-Friendly Diagnostics** — Structured diagnostic export for AI-agent operability.

---

## [0.1.4-preview] — 2026-02-22

### Phase 4: Application Shell

#### Added
- **Shell Policy Kit** — New window, download, permission, and session policies with typed contracts.
- **Multi-Window Lifecycle** — Orchestrated multi-window management with deterministic teardown.
- **Host Capability Bridge** — Clipboard, file dialog, external open, and notification capabilities.
- **Session & Permission Profiles** — Per-window and per-domain permission and session configuration.
- **Template Presets** — Shell configuration presets in `dotnet new agibuild-hybrid` template.
- **DevTools & Shortcuts** — Runtime DevTools toggle and keyboard shortcut integration.

---

## [0.1.3-preview] — 2026-02-18

### Phase 3: Polish & GA Preparation

#### Added
- **Project Template** — `dotnet new agibuild-hybrid` with React+Vite+TypeScript scaffold.
- **Vue Sample App** — Avalonia + Vue.js sample application alongside React sample.
- **GTK/Linux Smoke Validation** — Linux platform adapter stabilization and CI verification.
- **API Surface Review** — Pre-1.0 public API audit with naming convention validation.

#### Changed
- **Performance Benchmarks** — BenchmarkDotNet harness for bridge dispatch latency measurement.

---

## [0.1.2-preview] — 2026-02-15

### Phase 2: SPA Hosting

#### Added
- **Custom Protocol File Serving** — `app://` scheme handler for serving embedded SPA assets.
- **Embedded Resource Provider** — Serve SPA assets from embedded resources or filesystem.
- **Dev Mode HMR Proxy** — Hot Module Replacement proxy for development workflow.
- **SPA Router Fallback** — Client-side routing support with fallback to `index.html`.
- **Bridge + SPA Integration** — Type-safe bridge working within SPA-hosted applications.
- **React Sample App** — Avalonia + React reference application with Bridge integration.

---

## [0.1.1-preview] — 2026-02-12

### Phase 1: Type-Safe Bridge

#### Added
- **Source Generator (C# → JS)** — `[JsExport]` attribute and Roslyn source generator for typed C# service proxies.
- **Source Generator (JS → C#)** — `[JsImport]` attribute for importing JS services into C#.
- **TypeScript Declaration Generation** — Automatic `.d.ts` generation from `[JsExport]` interfaces.
- **Bridge Security** — Channel-isolated WebMessage routing with policy-based authorization.
- **MockBridge for Testing** — `MockBridgeService` for unit testing bridge interactions without a browser.

---

## [0.1.0-preview] — 2026-02-10

### Phase 0: Foundation

#### Added
- **Cross-Platform Adapters** — Windows (WebView2), macOS (WKWebView), Linux (WebKitGTK), Android (Android WebView), iOS (WKWebView).
- **Full-Control Navigation** — Async navigation with cancellation, history, and interception.
- **WebMessage Bridge** — Bidirectional JavaScript ↔ C# messaging with origin policy.
- **Cookie Management** — `ICookieManager` interface (experimental: AGWV001).
- **Command Manager** — Keyboard shortcut and command routing.
- **Screenshot & PDF** — `CaptureScreenshotAsync` and `PrintToPdfAsync` capabilities.
- **RPC Layer** — JSON-RPC 2.0 based bidirectional method invocation.
- **Zoom & Find** — `GetZoomFactorAsync`/`SetZoomFactorAsync` and `FindInPageAsync`.
- **Preload Scripts** — `AddPreloadScriptAsync` for document-start script injection.
- **Context Menu** — `ContextMenuRequested` event with media type and coordinates.
- **WebDialog** — Modal/modeless dialog-style WebView windows.
- **WebAuth Broker** — Platform-specific OAuth authentication flow support.
- **1459 Unit Tests + 209 Integration Tests** — Comprehensive contract and runtime test coverage.
