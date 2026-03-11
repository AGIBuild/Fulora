# Avalonia + React Hybrid Sample

Production-grade sample demonstrating **Agibuild.Fulora** with a React frontend — type-safe C# ↔ JS Bridge, SPA hosting, and Vite HMR development workflow.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (preview)
- [Node.js 20+](https://nodejs.org/) with npm
- macOS, Windows, or Linux

## Quick Start (Unified Build)

The solution includes a `AvaloniReact.Web` project that integrates npm lifecycle into MSBuild. A single `dotnet build` handles both C# compilation and frontend dependency installation.

```bash
cd samples/avalonia-react

# Build the entire solution (C# + npm install for the web frontend)
dotnet build
```

### Development Mode (HMR)

Development mode uses Vite dev server with hot module replacement — changes to React code reflect instantly.

```bash
# 1. Start Vite dev server (keep this running)
cd samples/avalonia-react/AvaloniReact.Web
npm run dev

# 2. In a new terminal, start the Avalonia desktop app
cd samples/avalonia-react
dotnet run --project AvaloniReact.Desktop
```

The desktop app opens with a WebView pointing to `http://localhost:5173`. Edit any `.tsx` file and see changes instantly via HMR.

> **Tip**: `dotnet build` automatically runs `npm ci` if needed, so you can skip manual `npm install`.

## Production Mode

Production mode embeds the React build output into the .NET assembly — no Node.js or dev server needed at runtime.

```bash
cd samples/avalonia-react

# Build and run in Release mode
# (MSBuild automatically runs `npm run build` via Web.csproj and embeds dist/ as resources)
dotnet run --project AvaloniReact.Desktop -c Release
```

## Running Tests

```bash
cd samples/avalonia-react
dotnet test
```

All 23 unit tests validate Bridge service implementations using `MockBridgeService` — no real WebView or browser needed.

## Project Structure

```
avalonia-react/
├── AvaloniReact.sln                   # Solution file
│
├── AvaloniReact.Bridge/               # Shared Bridge layer (no UI dependency)
│   ├── Models/                        # Data transfer records
│   │   ├── AppInfo.cs                 # App metadata
│   │   ├── AppSettings.cs            # User preferences
│   │   ├── ChatMessage.cs            # Chat models (Request/Response/Message)
│   │   ├── FileEntry.cs              # File system entry
│   │   ├── PageDefinition.cs         # Dynamic page registry entry
│   │   ├── RuntimeMetrics.cs         # Live process metrics
│   │   └── SystemInfo.cs             # Static platform info
│   └── Services/
│       ├── IAppShellService.cs        # [JsExport] Page registry + app info
│       ├── ISystemInfoService.cs      # [JsExport] OS/runtime data
│       ├── IChatService.cs            # [JsExport] Chat with history
│       ├── IFileService.cs            # [JsExport] File system access
│       ├── ISettingsService.cs        # [JsExport] Preferences CRUD
│       ├── IUiNotificationService.cs  # [JsImport] C# → JS notifications
│       ├── IThemeService.cs           # [JsImport] C# → JS theme change
│       └── *.cs                       # Implementations
│
├── AvaloniReact.Desktop/              # Avalonia host application
│   ├── Program.cs                     # Entry point + UseAgibuildWebView()
│   ├── MainWindow.axaml.cs           # SPA hosting + Bridge registration
│   └── AvaloniReact.Desktop.csproj   # Debug→Vite proxy / Release→embedded
│
├── AvaloniReact.Web/                  # React frontend (Vite + TypeScript)
│   ├── src/
│   │   ├── App.tsx                   # Dynamic routing from page registry
│   │   ├── bridge/services.ts        # Typed RPC proxies for all services
│   │   ├── hooks/                    # useBridge, usePageRegistry
│   │   ├── components/Layout.tsx     # Sidebar, dark mode, JsImport handlers
│   │   └── pages/                    # Dashboard, Chat, Files, Settings
│   ├── package.json                  # React 19, Vite 6, Tailwind CSS 4
│   └── vite.config.ts
│
└── AvaloniReact.Tests/                # 23 unit tests (xUnit)
```

## What This Sample Demonstrates

### Bridge Patterns

| Pattern | Service | Direction |
|---------|---------|-----------|
| **C# → JS** (JsExport) | `IAppShellService` | Dynamic page registry queried by React |
| **C# → JS** (JsExport) | `ISystemInfoService` | Native OS/runtime info unavailable in browsers |
| **C# → JS** (JsExport) | `IChatService` | Complex types, message history, async processing |
| **C# → JS** (JsExport) | `IFileService` | Native file system read access |
| **C# → JS** (JsExport) | `ISettingsService` | Preferences with JSON persistence |
| **JS → C#** (JsImport) | `IUiNotificationService` | C# triggers React toast notifications |
| **JS → C#** (JsImport) | `IThemeService` | C# triggers React theme changes |

### Pages

| Page | Features |
|------|----------|
| **Dashboard** | System info cards, live runtime metrics (auto-refresh every 2s) |
| **Chat** | Message input, echo bot with contextual replies, typing indicator, clear history |
| **Files** | Directory browser, breadcrumb navigation, text file preview panel |
| **Settings** | Theme / language / font size / sidebar toggle, JSON persistence |

### Architecture Highlights

- **Dynamic page registry**: Pages are defined in C# and fetched by React on startup — no hardcoded routes. Adding a page = add a component + register in `AppShellService`.
- **Dev/Prod mode switch**: `#if DEBUG` uses Vite HMR proxy; Release embeds Vite build output as .NET resources.
- **Typed RPC**: `bridge/services.ts` provides TypeScript proxies with full type safety for every C# service.
- **JsImport handlers**: `Layout.tsx` registers `rpc.handle()` for notification and theme services — C# can call into React.
- **Testable**: All services tested via `MockBridgeService` with no WebView dependency.

## Extending the Sample

### Adding a New Page

1. Create `src/pages/MyPage.tsx` in the React app
2. Register the component in `App.tsx`:
   ```typescript
   const PAGE_COMPONENTS: Record<string, React.ComponentType> = {
     // ... existing pages
     mypage: MyPage,
   };
   ```
3. Add a `PageDefinition` in `AppShellService.cs`:
   ```csharp
   new("mypage", "My Page", "Star", "/mypage"),
   ```

### Adding a New Bridge Service

1. Define the interface with `[JsExport]` in `AvaloniReact.Bridge/Services/`
2. Implement the service
3. Register in `MainWindow.axaml.cs`: `WebView.Bridge.Expose<IMyService>(new MyService())`
4. Add a TypeScript proxy in `bridge/services.ts`
5. Write unit tests

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Desktop host | Avalonia 11, .NET 10 |
| WebView | Agibuild.Fulora (WKWebView / WebView2 / WebKitGTK) |
| Bridge | JSON-RPC 2.0 over WebMessage, Roslyn source generator |
| Frontend | React 19, TypeScript 5, Vite 6, Tailwind CSS 4 |
| Icons | Lucide React |
| Tests | xUnit, MockBridgeService |
