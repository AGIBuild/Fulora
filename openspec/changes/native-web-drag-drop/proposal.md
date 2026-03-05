## Why

Drag-and-drop between native Avalonia UI and WebView content is completely absent — zero code exists. This is a critical gap because: (1) Fulora's competitive positioning claims "seamless native UI mixing" vs Electron/Tauri, (2) real productivity apps (file managers, design tools, dashboards) require cross-boundary drag-drop, and (3) both Electron and Tauri support this natively. Goal: G1 (Type-Safe Bridge), G2 (SPA Hosting) — enabling natural interaction between native and web layers.

## What Changes

- New adapter interface `IDragDropAdapter` for cross-boundary drag-drop events
- Windows adapter: leverage WebView2 Composition Controller drag APIs (`DragEnter`/`DragLeave`/`DragOver`/`Drop` on `ICoreWebView2CompositionController`)
- macOS adapter: extend WKWebView native shim with `NSDraggingDestination`/`NSDraggingSource`
- Bridge-level `IDragDropService` (`[JsExport]`) for web content to participate in native drag operations
- JS-side helpers in `@agibuild/bridge` for drag event interop
- `WebView` control integration: forward Avalonia `DragDrop` events to adapter

## Capabilities

### New Capabilities

- `native-web-drag-drop`: Cross-boundary drag-and-drop between Avalonia native controls and WebView content

### Modified Capabilities

- `webview-adapter-abstraction`: Add `IDragDropAdapter` facet
- `webview-core-contracts`: Add drag-drop event forwarding

## Non-goals

- Drag-drop between multiple WebView instances (use message bus)
- Custom drag preview rendering (use platform defaults)
- Touch-based drag on mobile (desktop-first, mobile follow-up)

## Impact

- New: `IDragDropAdapter` interface in `Agibuild.Fulora.Adapters.Abstractions`
- New: `IDragDropService` bridge service in `Agibuild.Fulora.Core`
- Modified: Windows adapter (composition controller migration for drag APIs)
- Modified: macOS native shim (`WkWebViewShim.mm`)
- Modified: `WebView.cs` (drag event forwarding)
- New: CT + IT coverage for drag-drop scenarios
