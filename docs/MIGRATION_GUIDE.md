# Migration Guide: Electron → Agibuild.Fulora

This guide helps teams migrate from Electron to Agibuild.Fulora for building cross-platform desktop applications with web UI.

---

## Architecture Comparison

| Aspect | Electron | Agibuild.Fulora |
|--------|----------|-----------------|
| Runtime | Bundled Chromium + Node.js | Platform-native WebView (WebView2, WKWebView, WebKitGTK) |
| App size | 150–250 MB | 5–15 MB (uses OS WebView) |
| Host language | JavaScript/TypeScript (main process) | C# (.NET) |
| UI framework | Any web framework | Any web framework |
| IPC mechanism | `ipcMain` / `ipcRenderer` | Type-safe Bridge (`[JsExport]` / `[JsImport]`) |
| Packaging | electron-builder / electron-forge | NuGet + `dotnet publish` |
| Auto-update | electron-updater | SPA Asset Hot Update (web layer) + OS package manager (native layer) |

---

## Step-by-Step Migration

### 1. Set Up the Fulora Project

```bash
dotnet new agibuild-hybrid -n MyApp
cd MyApp
```

This creates:
- `MyApp/` — C# host application (Avalonia + Fulora)
- `MyApp.Web.Vite.React/` — React + Vite frontend (replaceable with your existing web app)

### 2. Migrate Your Web Frontend

Copy your existing web application into the `MyApp.Web.Vite.React/` directory (or replace it entirely with your framework of choice).

**Key change**: Replace Electron's `preload.js` with the Fulora bridge client:

```bash
npm install @agibuild/fulora-client
```

If you are migrating from an earlier Fulora JavaScript integration, update imports from the old generic package names:

```diff
- import { createBridgeClient } from '@agibuild/bridge'
+ import { createBridgeClient } from '@agibuild/fulora-client'
```

### 3. Replace IPC with Bridge Services

**Electron (before):**

```typescript
// preload.js
const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('api', {
  readFile: (path) => ipcRenderer.invoke('read-file', path),
  saveFile: (path, data) => ipcRenderer.invoke('save-file', path, data),
});

// main.js
ipcMain.handle('read-file', async (event, path) => {
  return fs.readFileSync(path, 'utf-8');
});
```

**Fulora (after):**

```csharp
// C# — Define and implement the service
[JsExport]
public interface IFileService
{
    Task<string> ReadFile(string path);
    Task SaveFile(string path, string data);
}

public class FileService : IFileService
{
    public Task<string> ReadFile(string path) => File.ReadAllTextAsync(path);
    public Task SaveFile(string path, string data) => File.WriteAllTextAsync(path, data);
}

// Register in host setup
webView.Bridge.Expose<IFileService>(new FileService());
```

```typescript
// TypeScript — Consume the service (auto-generated types)
import { bridgeClient } from '@agibuild/fulora-client';
import type { IFileService } from './bridge'; // auto-generated .d.ts

const fileService = bridgeClient.getService<IFileService>('FileService');
const content = await fileService.readFile('/path/to/file');
```

### 4. Replace Window Management

**Electron:**
```javascript
const win = new BrowserWindow({ width: 800, height: 600 });
win.loadURL('https://example.com');
```

**Fulora:**
```csharp
var dialog = webViewEnvironment.CreateWebDialog();
dialog.Resize(800, 600);
await dialog.NavigateAsync(new Uri("https://example.com"));
dialog.Show();
```

### 5. Replace Menu and Context Menu

**Electron:**
```javascript
const menu = Menu.buildFromTemplate([...]);
Menu.setApplicationMenu(menu);
```

**Fulora:**
```csharp
webView.ContextMenuRequested += (sender, args) =>
{
    // Handle context menu with Avalonia's native menu system
};
```

### 6. Replace Auto-Update

**Electron** bundles the entire Chromium runtime in updates (~100 MB per update).

**Fulora** separates concerns:
- **Web layer**: Use `SpaAssetHotUpdateService` for atomic, signed SPA updates (typically < 5 MB)
- **Native layer**: Use OS package managers (MSI/MSIX, DMG, deb/rpm) for .NET runtime updates

### 7. Build and Package

```bash
# Build the web frontend
cd MyApp.Web.Vite.React
npm run build

# Build and publish the .NET app
cd ../MyApp
dotnet publish -c Release
```

---

## IPC → Bridge Mapping Reference

| Electron Pattern | Fulora Equivalent |
|-----------------|-------------------|
| `ipcMain.handle(channel, handler)` | `[JsExport] interface` + `Bridge.Expose<T>()` |
| `ipcRenderer.invoke(channel, ...args)` | `bridgeClient.getService<T>(name).method(params)` |
| `ipcRenderer.send(channel, ...args)` | `bridgeClient.invoke(method, params)` |
| `ipcMain.on(channel, handler)` | `Rpc.Handle(method, handler)` |
| `webContents.send(channel, ...args)` | `Rpc.InvokeAsync(method, args)` (C# → JS) |
| `contextBridge.exposeInMainWorld` | Automatic via `@agibuild/fulora-client` client |

---

## Feature Mapping

| Electron Feature | Fulora Feature | Notes |
|-----------------|----------------|-------|
| `BrowserWindow` | `WebView` (Avalonia control) | Embedded in Avalonia window |
| `BrowserWindow` (separate) | `IWebDialog` | Standalone dialog windows |
| `webContents.executeJavaScript` | `InvokeScriptAsync` | |
| `webContents.setZoomFactor` | `SetZoomFactorAsync` | |
| `webContents.findInPage` | `FindInPageAsync` | |
| `webContents.print/printToPDF` | `PrintToPdfAsync` | |
| `webContents.capturePage` | `CaptureScreenshotAsync` | |
| `session.cookies` | `TryGetCookieManager()` | Experimental (AGWV001) |
| `protocol.registerSchemeAsPrivileged` | `EnableSpaHosting()` | Built-in `app://` scheme |
| `autoUpdater` | `SpaAssetHotUpdateService` | Web layer only |
| `app.setAsDefaultProtocolClient` | `IDeepLinkRegistrationService` | OS-level URI scheme registration |

---

## Key Advantages After Migration

1. **90% smaller app size** — No bundled Chromium; uses the OS-provided WebView
2. **Type-safe IPC** — Compile-time verified C# ↔ JS contracts via source generators
3. **Native .NET ecosystem** — Full access to NuGet packages, Entity Framework, ML.NET, etc.
4. **Platform-native feel** — Avalonia UI toolkit with native rendering
5. **Faster updates** — SPA hot updates without replacing the entire app binary
6. **Better security** — OS-managed WebView with automatic security patches

---

## Common Pitfalls

1. **Node.js APIs**: Fulora runs C# on the host side, not Node.js. Replace `fs`, `path`, `child_process` with .NET equivalents (`System.IO`, `System.Diagnostics.Process`).

2. **Synchronous IPC**: Electron's `ipcRenderer.sendSync` has no equivalent. All Fulora bridge calls are async. Refactor synchronous IPC patterns to async/await.

3. **Preload scripts**: Replace Electron's `preload.js` with Fulora's `AddPreloadScriptAsync` for document-start injection, or use the bridge client which handles initialization automatically.

4. **Native modules**: Electron native modules (`.node` files) must be replaced with .NET P/Invoke or NuGet packages.

5. **Multi-window**: Electron creates separate `BrowserWindow` instances. Fulora uses `IWebDialog` for additional windows or multiple `WebView` controls within Avalonia windows.

## 1.5.x → 1.6.0 (P5: Capability Split)

**Nothing is required.** All changes are additive.

### What's new

You can now depend on a single WebView capability instead of the whole `IWebView` composite. This is especially useful for:

- **Testing**: stub only the interface you consume, e.g. `Mock<IWebViewZoom>` instead of full `IWebView`
- **Dependency injection**: register components against narrow contracts (`IWebViewDevTools`, `IWebViewScreenshot`, ...) so substitution works per-capability
- **Library authors**: expose capabilities on downstream types without implying they own the whole WebView surface

### Example

Before:

```csharp
public sealed class PdfExporter
{
    private readonly IWebView _webView; // overfits — we only need printing
    public PdfExporter(IWebView webView) => _webView = webView;
    public Task<byte[]> ExportAsync() => _webView.PrintToPdfAsync();
}
```

After (recommended for new code):

```csharp
public sealed class PdfExporter
{
    private readonly IWebViewPrinting _printing;
    public PdfExporter(IWebViewPrinting printing) => _printing = printing;
    public Task<byte[]> ExportAsync() => _printing.PrintToPdfAsync();
}
```

Both forms compile against the same `WebViewCore` / `WebDialog` / `WebView` instances — no implementation change is needed downstream.

### Full list of new capability interfaces

`IWebViewDevTools`, `IWebViewScreenshot`, `IWebViewPrinting`, `IWebViewZoom`, `IWebViewFindInPage`, `IWebViewPreloadScripts`, `IWebViewNativeHandle`, `IWebViewDownloads`, `IWebViewPermissions`, `IWebViewContextMenu`, `IWebViewPopupWindows`, `IWebViewResourceInterception`, `IWebViewLifecycleEvents`.

See `docs/API_SURFACE_REVIEW.md` for the full member mapping.
