## Purpose

Define framework-level shell-window service contracts, transparency state machine, multi-window unification, and host-owned drag semantics for hybrid apps.

## ADDED Requirements

### Requirement: IWindowShellService SHALL be a framework-level [JsExport] contract

The framework SHALL provide `IWindowShellService` as a `[JsExport]` interface in `Agibuild.Fulora.Core` with typed DTOs for shell-window state management.

#### Scenario: Service exposes snapshot, update, and stream methods
- **WHEN** the framework defines `IWindowShellService`
- **THEN** it SHALL include `GetWindowShellState()`, `UpdateWindowShellSettings(WindowShellSettings)`, and `StreamWindowShellState(CancellationToken)` methods
- **AND** the interface SHALL be annotated with `[JsExport]`

#### Scenario: Service is usable as app-level singleton
- **WHEN** an app creates a `WindowShellService` instance
- **THEN** the same instance SHALL be exposable to multiple WebViews via `Bridge.Expose<IWindowShellService>()`
- **AND** all WebViews SHALL observe the same global shell state

### Requirement: IWindowChromeProvider SHALL abstract platform-specific chrome operations

The framework SHALL define `IWindowChromeProvider` as a platform abstraction interface for transparency application, chrome metrics, and appearance change notifications.

#### Scenario: Provider reports platform identity and transparency support
- **WHEN** the provider is queried
- **THEN** it SHALL report `Platform` (string) and `SupportsTransparency` (bool)

#### Scenario: Provider applies appearance changes to tracked windows
- **WHEN** `ApplyWindowAppearanceAsync(request)` is called
- **THEN** the provider SHALL apply transparency, opacity, and theme-derived background to all tracked windows

#### Scenario: Provider notifies on OS-level appearance changes
- **WHEN** OS theme changes or compositor state changes
- **THEN** the provider SHALL raise `AppearanceChanged` event

### Requirement: TransparencyLevel SHALL be a formal enum

The framework SHALL define `TransparencyLevel` as an enum with values: `None`, `Transparent`, `Blur`, `AcrylicBlur`, `Mica`.

#### Scenario: Enum serializes as camelCase string for bridge
- **WHEN** `TransparencyLevel` is serialized through the bridge
- **THEN** it SHALL appear as a camelCase string value (e.g., `"blur"`, `"acrylicBlur"`)

### Requirement: Transparency state machine SHALL enforce legal state combinations

The `WindowShellService` SHALL enforce a formal transparency state machine with defined legal combinations.

#### Scenario: Disabled state
- **WHEN** `EnableTransparency` is `false`
- **THEN** state SHALL report `IsTransparencyEnabled=false`, `IsTransparencyEffective=false`, `EffectiveTransparencyLevel=None`

#### Scenario: Active state
- **WHEN** `EnableTransparency` is `true` and platform compositor supports transparency
- **THEN** state SHALL report `IsTransparencyEnabled=true`, `IsTransparencyEffective=true`, `EffectiveTransparencyLevel` as the platform-resolved level

#### Scenario: Fallback state
- **WHEN** `EnableTransparency` is `true` but platform does not support transparency
- **THEN** state SHALL report `IsTransparencyEnabled=true`, `IsTransparencyEffective=false`, `EffectiveTransparencyLevel=None`
- **AND** state SHALL include a non-empty `ValidationMessage` explaining the fallback

#### Scenario: Invalid combinations are never produced
- **WHEN** any state is built by the service
- **THEN** `IsTransparencyEnabled=false` with `IsTransparencyEffective=true` SHALL NOT occur
- **AND** `IsTransparencyEffective=true` with `EffectiveTransparencyLevel=None` SHALL NOT occur

### Requirement: Settings validation SHALL clamp values to valid ranges

The `WindowShellService` SHALL validate and clamp settings before applying them.

#### Scenario: OpacityPercent is clamped
- **WHEN** `UpdateWindowShellSettings` receives `GlassOpacityPercent` outside `[20, 95]`
- **THEN** the applied state SHALL contain the clamped value

#### Scenario: Invalid ThemePreference defaults to system
- **WHEN** `UpdateWindowShellSettings` receives an unrecognized `ThemePreference`
- **THEN** the applied state SHALL use `"system"` as the effective preference

### Requirement: Theme resolution SHALL follow preference rules

The `WindowShellService` SHALL resolve effective theme mode based on `ThemePreference` setting and OS theme.

#### Scenario: System preference follows OS theme
- **WHEN** `ThemePreference` is `"system"`
- **THEN** `EffectiveThemeMode` SHALL match the current OS theme mode (light/dark)

#### Scenario: Fixed preference ignores OS theme
- **WHEN** `ThemePreference` is `"liquid"` or `"classic"`
- **THEN** `EffectiveThemeMode` SHALL be `"liquid"` or `"classic"` respectively, regardless of OS theme

### Requirement: Stream SHALL deduplicate equivalent states

The `StreamWindowShellState()` method SHALL suppress duplicate emissions when the effective state has not changed.

#### Scenario: Repeated equivalent state produces single emission
- **WHEN** host notifications produce the same effective state signature consecutively
- **THEN** the stream SHALL emit only one event for the first occurrence

#### Scenario: State change after duplicate produces new emission
- **WHEN** a different effective state follows a sequence of duplicates
- **THEN** the stream SHALL emit the new state

### Requirement: Multi-window state SHALL be unified

Theme and transparency settings SHALL apply globally across all tracked windows.

#### Scenario: Settings update applies to all windows
- **WHEN** `UpdateWindowShellSettings` is called
- **THEN** the provider SHALL apply the appearance to ALL tracked windows

#### Scenario: New window inherits current settings
- **WHEN** a new window is tracked after settings have been configured
- **THEN** the provider SHALL apply current settings to the new window

### Requirement: WindowShellService SHALL be testable with mock provider

The service SHALL be constructable with mock `IWindowChromeProvider` and `IPlatformThemeProvider` for contract testing without a real platform.

#### Scenario: Mock provider roundtrip
- **WHEN** contract tests run with mock provider
- **THEN** `Update → Snapshot → Stream` behavior SHALL be deterministically verifiable
