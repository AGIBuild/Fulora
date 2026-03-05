# Native ↔ Web Drag and Drop — Tasks

## 1. Contracts & Abstractions

- [x] 1.1 Define `IDragDropAdapter` interface in `Agibuild.Fulora.Adapters.Abstractions`
- [x] 1.2 Define `DragDropPayload`, `FileDropInfo`, `DragDropEffects` types in Core
- [x] 1.3 Define `DragEventArgs`, `DropEventArgs` event types
- [x] 1.4 Add `IDragDropAdapter` as optional adapter facet
- [x] 1.5 CT: `DragDropPayload` construction and property verification
- [x] 1.6 CT: `DragDropEffects` flags combine correctly
- [x] 1.7 CT: `FileDropInfo` stores path and optional metadata
- [x] 1.8 Add `CreateWithDragDrop()` mock adapter variant

## 2. WebViewCore Integration

- [ ] 2.1 Add drag-drop event forwarding in `WebViewCore` — subscribe to adapter events
- [ ] 2.2 Expose `DragEntered`, `DragOver`, `DragLeft`, `DropCompleted` events on `WebViewCore`
- [ ] 2.3 Wire to `WebView.cs` Avalonia control

## 3. Windows Adapter (Phase 1: IDropTarget)

- [ ] 3.1 Spike: validate `IDropTarget`/`RegisterDragDrop` compatibility with WebView2
- [ ] 3.2 Implement `IDropTarget` on the WebView2 HWND
- [ ] 3.3 Extract `IDataObject` contents: files, text, HTML
- [ ] 3.4 Map to `DragDropPayload` and raise adapter events

## 4. macOS Adapter

- [ ] 4.1 Extend `WkWebViewShim.mm`: register for dragged types
- [ ] 4.2 Implement `NSDraggingDestination` methods
- [ ] 4.3 Extract file URLs and text from pasteboard
- [ ] 4.4 Forward to C# adapter

## 5. Bridge Service

- [ ] 5.1 Define `IDragDropBridgeService` (`[JsExport]`) with event-based API
- [ ] 5.2 Implement `DragDropBridgeService` consuming `WebViewCore` drag events
- [ ] 5.3 Add JS helpers to `@agibuild/bridge`
- [ ] 5.4 Add TypeScript types

## 6. Tests

- [ ] 6.1 CT: Windows `IDropTarget` mock with file data → payload extraction
- [ ] 6.2 CT: macOS pasteboard mock → payload extraction
- [ ] 6.3 CT: Bridge service delivers events to JS handler
- [ ] 6.4 Manual IT: drag file from Finder/Explorer onto WebView in sample app
