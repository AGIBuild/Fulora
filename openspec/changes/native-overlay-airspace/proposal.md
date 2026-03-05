## Why

Fulora's competitive positioning table claims "Native UI mixing: ✅ Seamless" vs Electron (❌ None) and Tauri (❌ None). In reality, the WebView is hosted via `NativeControlHost` which creates a separate native window surface — Avalonia controls cannot render on top of WebView content (the "airspace problem"). Solving this is critical to honoring the project's core differentiator. Goal: Core competitive positioning, G2 (SPA Hosting) — true hybrid UI where native overlays and web content coexist.

## What Changes

- Phase 1 (Practical): Transparent overlay window approach
  - Companion transparent window that floats above the WebView
  - Automatic position/size synchronization with the WebView bounds
  - Input routing: overlay captures input for its Avalonia controls, passes through to WebView otherwise
  - Cross-platform: Win32 layered window, macOS NSPanel, Linux X11/Wayland overlay
- Phase 2 (Future): WebView2 Composition Controller integration for Windows
  - Migrate Windows adapter from windowed hosting to `CreateCoreWebView2CompositionControllerAsync`
  - WebView renders into DirectComposition visual tree
  - Enables true composition with Avalonia rendering pipeline
- API: `WebView.OverlayContent` property accepting Avalonia `Control` for overlay placement

## Capabilities

### New Capabilities

- `native-overlay-airspace`: Native Avalonia controls rendered above WebView content

### Modified Capabilities

- `webview-adapter-abstraction`: Add overlay lifecycle hooks
- Windows adapter: composition controller option

## Non-goals

- Replacing NativeControlHost entirely
- GPU-composited blending of web and native content in Phase 1
- Mobile overlay (desktop-first)
- Transparent WebView background (separate concern)

## Impact

- New: `WebViewOverlayHost` in `Agibuild.Fulora.Avalonia`
- Modified: `WebView.cs` (OverlayContent property, overlay lifecycle)
- Modified: Windows adapter (Phase 2: composition controller migration)
- New: platform-specific overlay window implementations
- New: CT for overlay positioning and input routing
