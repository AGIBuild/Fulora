## Tasks

### Android Preload Script Injection Fix
- [x] In `AndroidWebViewAdapter.OnPageStarted`, inject all `_preloadScripts` after bridge script injection via `EvaluateJavascript`
- [x] Add unit test verifying preload scripts are injected on navigation

### macOS Native Overlay
- [x] Implement `MacOsNativeOverlayProvider` using ObjC runtime P/Invoke (NSPanel, borderless, non-activating, transparent, child window)
- [x] Add unit test for overlay lifecycle (platform-conditional)

### Linux Native Overlay
- [x] Implement `LinuxNativeOverlayProvider` using GTK3 P/Invoke (popup window, RGBA visual, transparent)
- [x] Add unit test for Linux overlay lifecycle (merged into overlay lifecycle test)

### iOS Drag-and-Drop
- [x] Add drag-drop callbacks to `ag_wk_callbacks` in `WkWebViewShim.iOS.mm`
- [x] Implement `UIDropInteractionDelegate` on the iOS WKWebView wrapper, forwarding to callbacks
- [x] Add `IDragDropAdapter` to `iOSWebViewAdapter` interface list with events and trampolines
- [x] Add unit test for drag-drop contract and payload types

### GTK PDF Printing
- [x] Implement `ag_gtk_print_to_pdf` in `WebKitGtkShim.c` using `webkit_print_operation_print()` with file-output settings
- [x] Add `IPrintAdapter` to `GtkWebViewAdapter` interface list and implement `PrintToPdfAsync`
- [x] Add unit test for PdfPrintOptions contract
