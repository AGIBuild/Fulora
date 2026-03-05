## Purpose

Define requirements for cross-boundary drag-and-drop between native Avalonia/OS content and WebView web content. Covers adapter interface, platform implementations, bridge service, and JS helpers.

## ADDED Requirements

### Requirement: IDragDropAdapter defines cross-boundary drag events

`IDragDropAdapter` SHALL be an adapter facet interface exposing drag-and-drop events.

#### Scenario: File dragged from OS file manager into WebView
- **GIVEN** a WebView with `IDragDropAdapter` support
- **WHEN** the user drags a file from the OS file manager over the WebView
- **THEN** the adapter SHALL raise `DragEntered` with a `DragDropPayload` containing file metadata
- **AND** SHALL raise `DragOver` as the cursor moves within the WebView bounds
- **AND** SHALL raise `DropCompleted` when the user releases the mouse

#### Scenario: Drag leaves WebView without drop
- **GIVEN** a drag operation in progress over the WebView
- **WHEN** the cursor leaves the WebView bounds without releasing
- **THEN** the adapter SHALL raise `DragLeft`

### Requirement: DragDropPayload carries typed data

`DragDropPayload` SHALL contain typed drag data accessible from both native and web code.

#### Scenario: File drop payload
- **GIVEN** one or more files are dropped on the WebView
- **WHEN** `DropCompleted` fires
- **THEN** `DragDropPayload.Files` SHALL contain `FileDropInfo[]` with `Name`, `Path`, `Size`, `MimeType`
- **AND** `DragDropPayload.Text` SHALL be null (file drop, not text drop)

#### Scenario: Text drop payload
- **GIVEN** text content is dragged from a native text box onto the WebView
- **WHEN** `DropCompleted` fires
- **THEN** `DragDropPayload.Text` SHALL contain the dragged text
- **AND** `DragDropPayload.Files` SHALL be empty

### Requirement: Bridge service delivers drag events to web content

`IDragDropBridgeService` (`[JsExport]`) SHALL bridge drag events to JavaScript.

#### Scenario: Web content receives drop event via bridge
- **GIVEN** web content has registered a drop handler via `bridge.dragDrop.onDrop(handler)`
- **WHEN** a file is dropped on the WebView
- **THEN** the handler SHALL receive a `DropEvent` with `{ files: FileInfo[], text?: string }`
- **AND** the handler SHALL be invoked on the next microtask

#### Scenario: Web content can accept or reject drag
- **GIVEN** web content has registered a drag-over handler via `bridge.dragDrop.onDragOver(handler)`
- **WHEN** a drag enters the WebView
- **THEN** the handler SHALL receive `{ effect: "copy" | "move" | "link" | "none" }`
- **AND** the handler can return the desired `dropEffect`

### Requirement: Windows adapter uses IDropTarget on WebView2 HWND

The Windows adapter SHALL implement `IDropTarget` on the WebView2 HWND to intercept OS drag-and-drop.

#### Scenario: File drop on Windows WebView2
- **GIVEN** a WebView2 adapter on Windows
- **WHEN** a file is dropped via OS drag-and-drop onto the WebView2 HWND
- **THEN** the adapter SHALL intercept the `IDropTarget.Drop` call
- **AND** SHALL extract file paths from `IDataObject`
- **AND** SHALL raise `DropCompleted` with the payload

### Requirement: macOS adapter uses NSDraggingDestination on WKWebView

The macOS adapter SHALL implement `NSDraggingDestination` on WKWebView to handle Finder drag-and-drop.

#### Scenario: File drop on macOS WKWebView
- **GIVEN** a WKWebView adapter on macOS
- **WHEN** a file is dropped via Finder drag-and-drop onto the WKWebView
- **THEN** the native shim SHALL handle `performDragOperation:`
- **AND** SHALL extract file URLs from `NSPasteboard`
- **AND** SHALL forward to the C# adapter as `DropCompleted`
