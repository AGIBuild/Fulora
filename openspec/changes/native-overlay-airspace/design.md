## Context

Fulora's `WebView` extends `NativeControlHost`, which creates a platform-native child window/view for the WebView surface. On Windows, this is an HWND (windowed WebView2 controller); on macOS, it's an NSView (WKWebView subview). Native windows are always rendered on top of the Avalonia visual tree — no Avalonia control can appear above the WebView. This is the "airspace problem" common to all WPF/Avalonia native control hosts.

**Platform specifics:**
- Windows WebView2: `CreateCoreWebView2ControllerAsync` (windowed) → separate HWND on top. `CreateCoreWebView2CompositionControllerAsync` → DirectComposition visual, composable.
- macOS WKWebView: NSView in the view hierarchy. Overlay possible via sibling NSView with higher z-order.
- Linux WebKitGTK: GtkWidget in GTK container. Overlay via GtkOverlay widget.

## Goals / Non-Goals

**Goals:**
- Phase 1: Transparent overlay window approach (cross-platform)
  - `WebViewOverlayHost` creates a companion window/view above the WebView
  - Overlay renders Avalonia controls (buttons, panels, tooltips, etc.)
  - Automatic position/size/visibility sync with the WebView bounds
  - Input passthrough: clicks not on overlay controls pass through to WebView
- Phase 2 (future): WebView2 composition controller on Windows
- API: `WebView.Overlay` property or `WebView.SetOverlay(Control content)` method

**Non-Goals:**
- Full GPU-composited blending
- Arbitrary z-ordering between web layers and native layers
- Mobile overlay
- Off-screen WebView rendering

## Decisions

### D1: Transparent companion window (Phase 1)

**Choice**: Create a separate top-level transparent window that:
1. Tracks the WebView's screen position and size
2. Renders Avalonia controls with transparent background
3. Stays always-on-top relative to the parent window
4. Routes input: hit-test Avalonia controls first; if no hit, forward to WebView

**Platform implementations:**
- Windows: `WS_EX_LAYERED | WS_EX_TRANSPARENT` + `SetLayeredWindowAttributes`. Use `WS_EX_TOOLWINDOW` to hide from taskbar. Child of main window.
- macOS: `NSPanel` with `isOpaque = false`, `backgroundColor = .clear`, `level = .floating`. Child of main NSWindow.
- Linux: GTK `gtk_window_set_type_hint(GDK_WINDOW_TYPE_HINT_UTILITY)` + RGBA visual for transparency.

**Rationale**: Most practical cross-platform approach. Doesn't require changes to WebView hosting or rendering pipeline. Used by VS Code, IntelliJ, and other apps for floating tool windows.

### D2: Position synchronization

**Choice**: Listen to WebView's `LayoutUpdated` and parent window's `PositionChanged`/`Resized` events. Update overlay window position/size synchronously. On Windows, additionally use `DeferWindowPos` for flicker-free moves.

**Rationale**: Must track pixel-perfectly. Layout events are the standard mechanism in Avalonia.

### D3: Input routing

**Choice**: Overlay window receives all input. Hit-test against Avalonia visual tree. If hit on a control → handle in overlay. If hit on transparent area → set `WS_EX_TRANSPARENT` (Windows) / `ignoresMouseEvents = true` (macOS temporarily) and re-send the event to the WebView.

**Alternative considered**: Always transparent to input, use `WS_EX_TRANSPARENT`. Problem: overlay controls wouldn't receive input.

**Rationale**: Selective input routing is more complex but necessary for overlay controls to be interactive.

### D4: API design

**Choice**:
```csharp
public partial class WebView : NativeControlHost
{
    public static readonly StyledProperty<Control?> OverlayContentProperty = ...;
    public Control? OverlayContent { get; set; }
}
```

Usage:
```xml
<fulora:WebView Source="app://localhost/index.html">
    <fulora:WebView.OverlayContent>
        <StackPanel HorizontalAlignment="Right" VerticalAlignment="Top" Margin="8">
            <Button Content="Native Button" Click="OnClick"/>
        </StackPanel>
    </fulora:WebView.OverlayContent>
</fulora:WebView>
```

**Rationale**: XAML-friendly, declarative, follows Avalonia conventions.

## Risks / Trade-offs

- **[Risk] Flicker during resize** → Use `DeferWindowPos` (Windows) and frame synchronization. May not be perfectly smooth.
- **[Risk] Multi-monitor DPI** → Overlay must match WebView DPI. Use per-monitor DPI awareness.
- **[Risk] Complexity** → Companion window approach is inherently complex. Phase 1 targets basic overlay; polish in follow-ups.
- **[Trade-off] Input routing latency** → Selective forwarding adds a hit-test per input event. Acceptable for UI interactions.

## Testing Strategy

- **CT**: Overlay position calculation tests (mock WebView bounds → expected overlay bounds)
- **CT**: Hit-test routing logic (hit on control → handled, hit on transparent → passthrough)
- **IT**: Manual visual validation on Windows and macOS
- **IT**: Input routing validation (click overlay button, click WebView through transparent area)
