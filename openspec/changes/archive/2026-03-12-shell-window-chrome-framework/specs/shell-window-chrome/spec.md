## Purpose

Refine existing shell-window-chrome spec with formalized state machine, platform applicability matrix, and drag exclusion zone contract.

## MODIFIED Requirements

### Requirement: Custom chrome drag behavior SHALL be host-owned and deterministic

The host SHALL own drag-region evaluation for custom chrome windows and SHALL apply deterministic precedence between drag initiation and interactive elements. Drag region handling SHALL be provided at the framework level via window-level pointer interception.

#### Scenario: Pointer in drag region starts window drag
- **GIVEN** a window tracked with custom chrome enabled and configured drag region height
- **WHEN** primary pointer press occurs within the drag region height from the top of the window
- **THEN** the framework SHALL initiate window drag operation

#### Scenario: Interactive control exclusion prevents drag initiation
- **GIVEN** interactive controls (Button, ToggleButton, TextBox, ComboBox, Slider) in the drag region
- **WHEN** pointer press targets an interactive control
- **THEN** drag SHALL NOT start
- **AND** pointer event SHALL be delivered to the interactive control

#### Scenario: Custom exclusion override
- **GIVEN** the provider is configured with a custom `IsInteractiveOverride` callback
- **WHEN** pointer press occurs in the drag region
- **THEN** the callback SHALL be consulted for exclusion decisions before default logic

#### Scenario: Drag region on platforms without window chrome
- **GIVEN** a mobile platform (Android/iOS) where window drag is not applicable
- **WHEN** the provider is queried for drag support
- **THEN** drag handling SHALL be a no-op with no errors

### Requirement: Shell window state SHALL expose applied/effective values

The system SHALL expose a typed shell-window state contract where returned values represent host-applied/effective state, not only requested settings.

#### Scenario: Snapshot returns effective transparency fields
- **WHEN** host provides shell-window state snapshot
- **THEN** state SHALL include `IsTransparencyEnabled`, `IsTransparencyEffective`, and `EffectiveTransparencyLevel` (as `TransparencyLevel` enum)
- **AND** state SHALL include applied opacity/alpha value used by host composition

#### Scenario: Snapshot returns chrome layout metrics
- **WHEN** web requests shell-window state snapshot
- **THEN** state SHALL include top chrome metrics (`TitleBarHeight`, `DragRegionHeight`, and `SafeInsets`)
- **AND** metrics SHALL be consumable by web layout without platform-specific inference logic

### Requirement: Shell window state synchronization SHALL be stream-first

The system SHALL provide event-stream based shell-window state synchronization with deterministic ordering and deduplication semantics.

#### Scenario: State changes are delivered through stream
- **WHEN** effective shell-window state changes due to host setting updates or OS theme changes
- **THEN** stream subscribers SHALL receive updated state without requiring periodic polling

#### Scenario: Equivalent state does not emit duplicate event
- **WHEN** host receives repeated notifications that produce the same effective state signature
- **THEN** stream SHALL suppress duplicate emissions

#### Scenario: Stream termination is observable
- **WHEN** the stream terminates due to service disposal or bridge disconnect
- **THEN** the web consumer SHALL receive a terminal signal via the async enumerable protocol

### Requirement: Transparency setting SHALL apply to both host window and web surface semantics

Transparency configuration SHALL be resolved by host and exposed as a single applied state that web UI can trust.

#### Scenario: Enable transparency updates applied state
- **WHEN** transparency is enabled and host compositor supports a transparent level
- **THEN** applied state SHALL report `IsTransparencyEnabled=true`, `IsTransparencyEffective=true`
- **AND** `EffectiveTransparencyLevel` SHALL be a non-None `TransparencyLevel` value

#### Scenario: Transparency unsupported reports deterministic fallback
- **WHEN** transparency is enabled but host/compositor resolves to non-transparent level
- **THEN** applied state SHALL report `IsTransparencyEnabled=true`, `IsTransparencyEffective=false`
- **AND** state SHALL include deterministic `ValidationMessage` explaining the fallback reason
