## Context

Shell/window chrome behavior (transparency, theme following, drag regions, chrome metrics) is duplicated in each sample app. The `AppearanceService` in `avalonia-ai-chat` contains ~300 lines of platform-specific logic that every new app must replicate. DTOs (`WindowShellState`, `WindowShellSettings`, etc.) already exist in `Agibuild.Fulora.Core`, but the service contract (`IWindowShellService`) and all behavior logic live in the sample layer.

Existing framework patterns to follow:
- `IThemeService` + `IPlatformThemeProvider` + `ThemeService` — OS theme detection (Core contract + Runtime implementation + platform abstraction)
- `WebViewHostCapabilityBridge` — policy-driven host capability execution pattern
- `IAsyncEnumerable<T>` streaming via `bridge-async-enumerable` protocol

Roadmap alignment: Phase 4 / Deliverables 4.2 (shell lifecycle), 4.5 (shell DX), 4.6 (hardening). Post-roadmap maintenance as platform-hardening work.

## Goals / Non-Goals

**Goals**
- Provide a framework-level `IWindowShellService` that apps consume with ~5 lines of setup code.
- Unify theme and transparency state across multiple windows via a singleton service + multi-window provider.
- Formalize transparency as a state machine with defined legal state combinations.
- Provide host-owned drag region handling at the framework level (no app AXAML required).
- Keep all behavior testable via MockAdapter/contract tests without a real browser.

**Non-Goals**
- Replacing `IThemeService` (OS theme detection remains separate from app appearance state).
- Mobile (Android/iOS) drag regions (graceful no-op/fallback).
- App-specific settings UI (framework provides state contract, not UI).
- Authorization/policy for `UpdateWindowShellSettings` (trusted app web content; value clamping is sufficient).

## Decisions

### D1: Promote IWindowShellService to Core as [JsExport] contract

Move `IWindowShellService` from `AvaloniAiChat.Bridge` to `Agibuild.Fulora.Core`. The interface uses `[JsExport]` and references Core DTOs only.

**Alternative considered**: Keep in Runtime — rejected because Runtime depends on Core, and the interface must be referenceable by any Bridge project without Runtime dependency.

### D2: Introduce IWindowChromeProvider as platform abstraction

New interface in Core following the `IPlatformThemeProvider` pattern:

```csharp
public interface IWindowChromeProvider
{
    string Platform { get; }
    bool SupportsTransparency { get; }
    Task ApplyWindowAppearanceAsync(WindowAppearanceRequest request);
    TransparencyEffectiveState GetTransparencyState();
    WindowChromeMetrics GetChromeMetrics();
    event EventHandler? AppearanceChanged;
}
```

Platform-specific concerns (window tracking, drag regions) live on concrete implementations, not the interface.

**Alternative considered**: Put window tracking on the interface via `object` parameter — rejected to keep Core host-framework-neutral.

### D3: WindowShellService as singleton in Runtime

`WindowShellService` is created once per app and shared across all WebViews via `Bridge.Expose<IWindowShellService>()`. It coordinates:
- `IWindowChromeProvider` for platform operations
- `IPlatformThemeProvider` for OS theme detection
- Global `WindowShellSettings` state
- Theme resolution: `"system"` → follows OS, `"liquid"` / `"classic"` → fixed
- Transparency state machine with legal state combinations
- Stream with signature-based dedup

### D4: Formalized transparency state machine

Legal state combinations:

| enabled | effective | level | meaning |
|---------|-----------|-------|---------|
| false | false | None | Disabled |
| true | true | Blur/Mica/... | Active |
| true | false | None | Requested but platform doesn't support, with ValidationMessage |

Invalid combinations (runtime enforced): `enabled=false, effective=true` and `effective=true, level=None`.

Settings validation: `OpacityPercent` clamped to `[20, 95]`, `ThemePreference` restricted to `"system"` / `"liquid"` / `"classic"`.

### D5: TransparencyLevel as enum

Replace ad-hoc strings with a formal enum: `None`, `Transparent`, `Blur`, `AcrylicBlur`, `Mica`. Serialized as string for bridge compatibility. Existing `WindowShellCapabilities.EffectiveTransparencyLevel` changes from `string` to `TransparencyLevel` (serialized as camelCase string).

### D6: Avalonia drag region via tunnel PointerPressed handler

`AvaloniaWindowChromeProvider.TrackWindow(window, options)` registers a window-level `PointerPressedEvent` tunnel handler that:
1. Checks pointer Y position against `DragRegionHeight`
2. Excludes interactive controls (Button, ToggleButton, TextBox, ComboBox, Slider)
3. Calls `window.BeginMoveDrag(e)` for drag-eligible presses

No AXAML modifications needed. The provider also sets `ExtendClientAreaToDecorationsHint = true` when custom chrome is enabled.

**Alternative considered**: Inject a Border overlay — rejected because it modifies visual tree and conflicts with app layouts.

### D7: Multi-window unification

`AvaloniaWindowChromeProvider` tracks multiple windows. When `WindowShellService.UpdateWindowShellSettings()` is called, the provider applies appearance changes to ALL tracked windows simultaneously. Theme and transparency state are global per-app, not per-window.

Chrome metrics are derived from the primary (first tracked) window.

### D8: Stream recovery semantics

`IAsyncEnumerable<WindowShellState>` follows existing bridge-async-enumerable protocol. On stream termination (adapter crash, bridge disconnect), web receives `OperationCanceledException` via the enumerator protocol. Recovery: web calls `StreamWindowShellState()` again to re-subscribe.

No polling fallback in framework — the stream-first contract is complete.

## Risks / Trade-offs

- **Breaking change on EffectiveTransparencyLevel type**: Changing from `string` to `TransparencyLevel` enum. Mitigation: bridge serializes enum as string, so JS consumers see no difference. C# consumers need minor update.
- **Platform divergence**: Different compositors produce different transparency levels. Mitigation: `TransparencyEffectiveState` includes diagnostic `ValidationMessage` and reports actual effective level.
- **Drag region coordinate-based detection**: Pointer Y check depends on window coordinate system. Edge case: maximized windows on some platforms shift content. Mitigation: use `e.GetPosition(window)` which accounts for decoration offsets.

## Testing Strategy

- **CT (MockAdapter/Mock provider)**: State machine transitions, stream dedup, settings validation/clamping, theme resolution, legal/illegal state combinations.
- **CT (Drag semantics)**: `IsInteractiveElement` logic tested with mock visual tree hierarchy.
- **IT (Avalonia platform)**: Transparency application, custom chrome drag strip, multi-window state sync.
