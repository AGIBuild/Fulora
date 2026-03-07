## Why

Post v1.1.0 release, several platform adapter functional gaps remain:

1. **macOS/Linux Native Overlay** — `MacOsNativeOverlayProvider` and `LinuxNativeOverlayProvider` are pure stubs (no-op). Windows implementation exists and works. This breaks any scenario requiring Avalonia UI overlaid on WebView (dropdowns, tooltips, floating toolbars) on macOS/Linux.
2. **Android Preload Script Injection** — `AddPreloadScript` stores scripts in `_preloadScripts` dictionary but they are never injected. `OnPageStarted` only calls `InjectBridgeScript`, ignoring preload scripts entirely.
3. **iOS Drag-and-Drop** — iOS adapter does not implement `IDragDropAdapter`. macOS adapter has full NSDraggingDestination support. iPad drag-and-drop is a core interaction pattern.
4. **GTK PDF Printing** — `IPrintAdapter` is not implemented. The current comment claims WebKitGTK lacks PDF export, but `webkit_print_operation_print()` with file-output settings can produce headless PDF.

These are not polish items — they are functional capabilities that are either broken or absent on specific platforms.

## What Changes

- Implement `MacOsNativeOverlayProvider` using NSPanel via ObjC runtime interop in the existing native shim
- Implement `LinuxNativeOverlayProvider` using GTK popup window via the existing native shim
- Fix Android preload script injection in `OnPageStarted`
- Implement `IDragDropAdapter` on iOS adapter with `UIDropInteraction` in the native shim
- Implement `IPrintAdapter` on GTK adapter using `webkit_print_operation_print()`

## Non-goals

- MAUI host adapter (deferred)
- Windows context menu wiring (minor)
- DevTools close on Windows (platform limitation)

## Capabilities

### Modified Capabilities
- `native-overlay-airspace` — macOS/Linux implementation
- `native-web-drag-drop` — iOS implementation

### New Capabilities
(none — filling gaps in existing capabilities)

## Impact

- **Native shims**: macOS `.mm`, iOS `.mm`, GTK `.c` modified
- **C# adapters**: Android, iOS, GTK adapters modified
- **Avalonia host**: macOS/Linux overlay providers implemented
- **Tests**: unit tests for each fix
