# Bridge DevTools Panel

The Bridge DevTools Panel is an in-app debug overlay that displays real-time bridge call logs, payloads, latency metrics, and error details. It is designed for development use only.

## Enabling

### In the template

The `agibuild-hybrid` template includes the overlay by default (gated behind `#if DEBUG`):

```xml
<Grid RowDefinitions="*,Auto">
    <wv:WebView x:Name="WebView" Grid.Row="0" />
    <wv:BridgeDevToolsOverlay x:Name="DevToolsOverlay" Grid.Row="1"
        Height="300" IsVisible="False" />
</Grid>
```

```csharp
#if DEBUG
DevToolsOverlay.Attach(WebView);
DevToolsOverlay.RegisterToggleShortcut(this,
    new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift));
#endif
```

Toggle with **Ctrl+Shift+D** (or **Cmd+Shift+D** on macOS).

### Manual integration

```csharp
var devTools = new BridgeDevToolsService();
webView.BridgeTracer = devTools.Tracer;

// After the overlay WebView loads:
devTools.StartPushing(script => overlayWebView.InvokeScriptAsync(script));

// To stop:
devTools.StopPushing();
devTools.Dispose();
```

## What it shows

| Column      | Description                                       |
|-------------|---------------------------------------------------|
| Direction   | `JS→C#` (export) or `C#→JS` (import)             |
| Phase       | `Start`, `End`, `Error`, `ServiceExposed/Removed` |
| Service     | RPC service name                                  |
| Method      | RPC method name                                   |
| Latency     | Elapsed milliseconds                              |
| Params      | JSON payload (expandable, truncated to 4 KB)      |
| Result      | Return value or error message (expandable)        |

Error entries are highlighted in red.

## Architecture

```
RuntimeBridgeService
    └─ TracingRpcWrapper → IBridgeTracer
                               └─ DevToolsPanelTracer
                                      ├─ BridgeEventCollector (ring buffer)
                                      │     └─ subscriber → invokeScript → Overlay UI
                                      └─ optional inner tracer (chaining)
```

- `BridgeEventCollector`: Thread-safe bounded ring buffer (default 500 entries). Oldest entries are dropped on overflow; `DroppedCount` tracks discards.
- `DevToolsPanelTracer`: Implements `IBridgeTracer`, writes to the collector, optionally forwards to an inner tracer.
- `TracingRpcWrapper`: Wraps `IWebViewRpcService` to instrument export call handlers and import call invocations with `Stopwatch`-based latency measurement.
- `BridgeDevToolsService`: Entry point. Creates the tracer + collector pair, loads the overlay HTML from embedded resources, pushes events to the overlay via `invokeScript`.

## Configuration

| Property / Parameter | Default | Description                              |
|----------------------|---------|------------------------------------------|
| `bufferCapacity`     | 500     | Max events in the ring buffer            |
| Payload truncation   | 4 KB    | Large params/result fields are truncated |

## Related Tools

| Tool | Description |
|---|---|
| **BridgeCallProfiler** | Statistical aggregation of bridge call latency (P50/P95/P99), error rates per service/method. See `CompositeBridgeTracer` to combine with DevTools panel. |
| **OpenTelemetry Tracer** | Export bridge call spans and metrics to OTLP backends via `Agibuild.Fulora.Telemetry.OpenTelemetry`. |
| **VS Code Extension** | `agibuild-fulora` VS Code extension with live bridge call visualization in the IDE sidebar, connected via WebSocket debug protocol (`BridgeDebugServer`). |

## Production safety

The panel adds no overhead when not wired in. `NullBridgeTracer` (the default) short-circuits all tracing paths. Do not ship the overlay in release builds.
