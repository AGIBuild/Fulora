## Why

Shell/window chrome behavior (transparency, theme sync, drag regions, chrome metrics) is currently implemented per-sample in app code (`AppearanceService`, `MainWindow.axaml`). Every new app must duplicate ~300 lines of platform-specific logic for transparency state resolution, drag handling, interactive exclusion, and stream dedup. This violates the framework's promise of reducing host-app boilerplate (Phase 4 objectives) and makes multi-window theme/transparency unification impossible at the sample level.

This change advances:
- **G1** (typed bridge) — promotes `IWindowShellService` from sample to framework-level `[JsExport]` contract.
- **G4** (testability) — makes shell-window behavior testable via MockAdapter with formal state machine semantics.
- **Phase 4 / 4.2, 4.5, 4.6** — completes shell lifecycle, DX presets, and hardening by providing framework-owned chrome management.

## What Changes

- **Promote** `IWindowShellService` from sample-level (`AvaloniAiChat.Bridge`) to `Agibuild.Fulora.Core` as a framework `[JsExport]` contract.
- **Add** `IWindowChromeProvider` platform abstraction in Core for transparency application, chrome metrics, and appearance change notifications.
- **Add** `TransparencyLevel` enum and formalized transparency state types to replace ad-hoc strings.
- **Implement** `WindowShellService` in Runtime as an app-level singleton managing global settings, theme resolution, transparency state machine, and stream dedup.
- **Implement** `AvaloniaWindowChromeProvider` in the Avalonia layer with multi-window tracking, tunnel-based drag handling, transparency application, and OS theme monitoring.
- **Migrate** sample `AppearanceService` to consume the framework service, removing ~300 lines of duplicated logic.

## Non-goals

- Replacing `IThemeService` — it remains the OS theme detection contract; `IWindowShellService` is app appearance state.
- Mobile (Android/iOS) drag regions — mobile platforms don't have window chrome; the provider returns no-op/fallback gracefully.
- App-specific settings UI — the framework provides the state contract, not the UI.

## Capabilities

### New Capabilities

- `shell-window-chrome-framework`: Framework-owned shell-window service with typed contracts, transparency state machine, multi-window unification, and host-owned drag semantics.

### Modified Capabilities

- `shell-window-chrome`: Requirements refined with formalized state machine rules, platform applicability matrix, and exclusion zone contract.

## Impact

- **Core**: New `IWindowShellService`, `IWindowChromeProvider`, `TransparencyLevel`, supporting types.
- **Runtime**: New `WindowShellService` in `Shell/` directory.
- **Avalonia**: New `AvaloniaWindowChromeProvider` with multi-window + drag support.
- **Sample**: `AppearanceService` replaced by framework service; `MainWindow.axaml` simplified (no manual drag region).
- **Tests**: New CT for state machine, stream dedup, drag semantics; existing tests updated.
