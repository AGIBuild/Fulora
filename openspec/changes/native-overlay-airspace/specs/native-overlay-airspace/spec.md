## Purpose

Define requirements for native Avalonia controls rendered above WebView content via companion overlay window. Ensures cross-platform overlay positioning, input routing, and lifecycle management.

## ADDED Requirements

### Requirement: WebView.OverlayContent renders Avalonia controls above WebView

`WebView.OverlayContent` SHALL render Avalonia controls in a companion overlay window above web content.

#### Scenario: Overlay button appears above web content
- **GIVEN** a WebView with `OverlayContent` set to a `Button`
- **WHEN** the WebView navigates to a page
- **THEN** the Button SHALL be visually rendered above the web content
- **AND** SHALL be interactive (clickable)

#### Scenario: OverlayContent set to null removes overlay
- **GIVEN** a WebView with an active overlay
- **WHEN** `OverlayContent` is set to `null`
- **THEN** the overlay window SHALL be hidden
- **AND** the WebView SHALL receive all input directly

### Requirement: Overlay tracks WebView position and size

The overlay window SHALL track the WebView's position and size and stay pixel-aligned.

#### Scenario: WebView moves within the window
- **GIVEN** a WebView with an active overlay
- **WHEN** the WebView's position or size changes (e.g., layout update, splitter resize)
- **THEN** the overlay window SHALL update its position and size to match
- **AND** the update SHALL occur within the same frame (no visible lag)

#### Scenario: Parent window moves
- **GIVEN** a WebView with an active overlay in a movable window
- **WHEN** the user drags the parent window
- **THEN** the overlay SHALL track the window position
- **AND** SHALL remain pixel-aligned with the WebView

#### Scenario: WebView becomes invisible
- **GIVEN** a WebView with an active overlay
- **WHEN** the WebView is hidden (IsVisible = false) or its tab is deactivated
- **THEN** the overlay window SHALL be hidden

### Requirement: Input routing — overlay controls receive input, transparent areas pass through

Overlay controls SHALL receive input; transparent areas SHALL pass input through to the WebView.

#### Scenario: Click on overlay button
- **GIVEN** a WebView with an overlay containing a Button at the top-right
- **WHEN** the user clicks on the Button
- **THEN** the Button's Click event SHALL fire
- **AND** the WebView SHALL NOT receive the click

#### Scenario: Click on transparent area of overlay
- **GIVEN** a WebView with an overlay containing a small Button (not covering the full area)
- **WHEN** the user clicks on a transparent area of the overlay (not on the Button)
- **THEN** the click SHALL pass through to the WebView
- **AND** the web content SHALL receive the click event

#### Scenario: Keyboard input routing
- **GIVEN** a WebView with an overlay containing a TextBox
- **WHEN** the TextBox has focus and the user types
- **THEN** keyboard input SHALL go to the TextBox, not the WebView
- **WHEN** the TextBox loses focus
- **THEN** keyboard input SHALL go to the WebView

### Requirement: Platform-specific overlay implementation

Each platform SHALL use native overlay mechanisms (layered window on Windows, NSPanel on macOS).

#### Scenario: Windows overlay uses layered window
- **GIVEN** a Windows platform
- **WHEN** an overlay is activated
- **THEN** a `WS_EX_LAYERED` transparent child window SHALL be created
- **AND** it SHALL be positioned as a child of the main application window

#### Scenario: macOS overlay uses NSPanel
- **GIVEN** a macOS platform
- **WHEN** an overlay is activated
- **THEN** an `NSPanel` with transparent background SHALL be created
- **AND** it SHALL be a child panel of the main NSWindow

### Requirement: Multi-monitor DPI awareness

Overlays SHALL render correctly on high-DPI and multi-monitor setups.

#### Scenario: WebView on a high-DPI monitor
- **GIVEN** a WebView on a 200% DPI monitor
- **WHEN** an overlay is activated
- **THEN** the overlay SHALL render at the correct DPI
- **AND** Avalonia controls SHALL appear sharp (not blurry or double-sized)
