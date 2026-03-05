# Native Overlay / Airspace — Tasks

## 1. Core Overlay Infrastructure

- [x] 1.1 Create `WebViewOverlayHost` class in `Agibuild.Fulora.Avalonia` — manages companion overlay
- [x] 1.2 Add `OverlayContent` styled property to `WebView` control
- [x] 1.3 Implement overlay lifecycle: create on `OverlayContent` set, destroy on null
- [x] 1.4 CT: WebViewOverlayHost construction, Content get/set, Dispose

## 2. Position & Size Synchronization

- [x] 2.1 Implement `UpdatePosition(Rect, Point, double)` for screen coordinate calculation
- [x] 2.2 Implement `Show()` / `Hide()` visibility toggling
- [ ] 2.3 Handle DPI scaling — overlay matches WebView DPI
- [ ] 2.4 Handle WebView visibility changes — show/hide overlay in sync
- [ ] 2.5 Subscribe to `WebView.LayoutUpdated` and parent window position events

## 3. Input Routing

- [ ] 3.1 Implement hit-test: overlay receives input → test against Avalonia visual tree
- [ ] 3.2 If hit on Avalonia control → handle in overlay
- [ ] 3.3 If hit on transparent area → forward input to WebView
- [ ] 3.4 Handle keyboard focus routing between overlay and WebView

## 4. Windows Platform Implementation

- [ ] 4.1 Create overlay as child window with `WS_EX_LAYERED | WS_EX_TOOLWINDOW`
- [ ] 4.2 Use `SetLayeredWindowAttributes` for transparency
- [ ] 4.3 Use `DeferWindowPos` for flicker-free position updates
- [ ] 4.4 Handle `WM_NCHITTEST` for input passthrough on transparent areas

## 5. macOS Platform Implementation

- [ ] 5.1 Create overlay as `NSPanel` with `isOpaque = false`, `backgroundColor = .clear`
- [ ] 5.2 Set panel level and parent window relationship
- [ ] 5.3 Implement `ignoresMouseEvents` toggling for input passthrough

## 6. Linux Platform Implementation

- [ ] 6.1 Create overlay using GTK `gtk_window_set_type_hint(UTILITY)` + RGBA visual
- [ ] 6.2 Position tracking via GTK signals
- [ ] 6.3 Input passthrough via X11/Wayland shape regions

## 7. Integration Tests

- [ ] 7.1 Manual IT: overlay button clickable, web content clickable through transparent area (Windows)
- [ ] 7.2 Manual IT: same on macOS
- [ ] 7.3 Manual IT: overlay tracks WebView during window resize and move
- [ ] 7.4 Manual IT: multi-monitor DPI transition
