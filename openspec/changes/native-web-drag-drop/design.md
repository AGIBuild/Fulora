## Context

Fulora's `WebView` inherits from Avalonia `NativeControlHost`, which creates a separate native window/view for the WebView. Avalonia's DragDrop events are not delivered to `NativeControlHost` children — the native surface handles its own input. Currently zero drag-drop code exists in the project.

**Platform investigation findings:**
- **WebView2 (Windows)**: `ICoreWebView2CompositionController` exposes `DragEnter`/`DragLeave`/`DragOver`/`Drop` + `ICoreWebView2CompositionController5.DragStarting`. Requires composition hosting (not current windowed hosting).
- **WKWebView (macOS)**: Supports `NSDraggingDestination`/`NSDraggingSource` protocols on the view. Current native shim does not implement these.
- **WebKitGTK (Linux)**: Supports GTK drag-and-drop via `GtkWidget` signals (`drag-data-received`, `drag-drop`).
- **Avalonia NativeControlHost**: Does not relay drag events to hosted controls.

## Goals / Non-Goals

**Goals:**
- `IDragDropAdapter` interface as a new adapter facet
- Native → Web: files/data dragged from Avalonia TreeView (or OS file manager) onto WebView triggers HTML5 `drop` event
- Web → Native: drag initiated in WebView can be intercepted by adapter and forwarded to Avalonia `DragDrop`
- Platform implementations for Windows, macOS (Linux stub)
- Bridge service `IDragDropBridgeService` for web content to register drop zones and receive typed drag data
- JS helpers in `@agibuild/bridge` for drag event handling

**Non-Goals:**
- Mobile drag-drop (desktop platforms only)
- Custom drag ghost/preview rendering
- Cross-WebView drag (use message bus)
- Drag within WebView only (already works natively)

## Decisions

### D1: Two-phase implementation — OS-level first, Bridge-level second

**Choice**: Phase 1 delivers OS-level drag-drop (files from OS/native into WebView, and out). Phase 2 adds typed bridge-level drag service for fine-grained control.

**Rationale**: OS-level drag-drop covers 80% of use cases (file drop into web UI) with lower complexity. Bridge-level control is an enhancement.

### D2: Windows adapter stays windowed hosting for now

**Choice**: For Phase 1, use Win32 `IDropTarget` / `RegisterDragDrop` on the WebView2 HWND rather than migrating to composition controller. Composition controller migration is tracked separately in native-overlay-airspace.

**Rationale**: Composition controller migration is a large change with implications for input handling, sizing, and rendering. Drag-drop via `IDropTarget` on the existing HWND is simpler and sufficient for Phase 1. The composition migration (needed for airspace) will unlock composition-level drag APIs later.

### D3: macOS uses NSDraggingDestination on WKWebView

**Choice**: Extend the native shim to register the WKWebView as a dragging destination. Forward `draggingEntered:`, `performDragOperation:` callbacks to the C# adapter via existing callback mechanism.

**Rationale**: Standard macOS drag-drop pattern. WKWebView already supports HTML5 drag-drop internally; we need to bridge the boundary for native-originated drags.

### D4: IDragDropAdapter interface

**Choice**:
```
IDragDropAdapter:
  event DragStarting(DragStartingEventArgs)
  event DragEntered(DragEventArgs)
  event DragOver(DragEventArgs)
  event DragLeft()
  event DropCompleted(DropEventArgs)
  SetDropEffects(DragDropEffects)
```

**Rationale**: Mirrors standard drag-drop event patterns. Adapter fires events; `WebViewCore` and bridge service consume them.

### D5: File drop converts to bridge-friendly format

**Choice**: When files are dropped, adapter extracts file paths and metadata, packages as `DragDropPayload { Files: FileInfo[], Text?: string, Html?: string }`, and delivers to bridge service.

**Rationale**: Web content receives typed data (file names, sizes, MIME types) without direct file system access. Actual file reading goes through FileSystem plugin if needed.

## Risks / Trade-offs

- **[Risk] Platform behavior divergence** → Each platform has different drag-drop timing and data format. Normalize in adapter layer.
- **[Risk] WebView2 windowed hosting limitations** → `IDropTarget` on HWND may conflict with WebView2's internal drag handling. Spike needed.
- **[Trade-off] No composition controller yet** → Defers full drag API access on Windows to airspace change.

## Testing Strategy

- **CT**: `IDragDropAdapter` contract tests with mock adapter
- **CT**: `DragDropPayload` serialization/deserialization
- **IT**: Manual drag-drop testing on Windows and macOS with sample app
- **Spike**: Validate Win32 `IDropTarget` compatibility with WebView2 windowed hosting
