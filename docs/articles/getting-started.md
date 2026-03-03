# Getting Started

Build your first **framework-ready** hybrid app with Avalonia + web UI.

This guide follows the current product direction (through Phase 11: Ecosystem & Developer Experience):

- typed bridge contracts
- typed capability gateway
- policy-first runtime behavior
- automation-friendly diagnostics
- web-first template architecture
- official bridge plugins (database, http, file system, notifications, auth)
- OpenTelemetry observability
- VS Code bridge extension

## Prerequisites

- .NET 10 SDK
- Platform runtime:
  - **Windows**: WebView2 (usually installed with Edge)
  - **macOS/iOS**: WKWebView (built-in)
  - **Android**: Android WebView (built-in)
  - **Linux**: WebKitGTK (`libwebkit2gtk-4.1`)

## Recommended Path: Template Workflow

Use this path for most teams. It matches the recommended architecture with minimal host glue.

```bash
# Install CLI and template (once)
dotnet tool install -g Agibuild.Fulora.Cli
dotnet new install Agibuild.Fulora.Templates

# Create app (interactive frontend selection)
fulora new MyApp --frontend react

# Start dev servers (Vite + Avalonia together)
cd MyApp
fulora dev
```

Or use `dotnet new` directly:

```bash
dotnet new agibuild-hybrid -n MyApp
cd MyApp
dotnet run --project MyApp.Desktop
```

What you get immediately:

- ready-to-run host + web structure
- typed bridge contract wiring
- web-first development flow
- production-oriented project layout

## Manual Path: Build from Scratch

Use this when you need full control over project composition.

### 1) Create an Avalonia app

```bash
dotnet new avalonia.app -n MyApp
cd MyApp
```

### 2) Add package

```bash
dotnet add package Agibuild.Fulora
```

### 3) Add WebView control

```xml
<!-- MainWindow.axaml -->
<Window xmlns:wv="clr-namespace:Agibuild.Fulora;assembly=Agibuild.Fulora">
    <wv:WebView x:Name="WebView" />
</Window>
```

### 4) Navigate to a page

```csharp
// MainWindow.axaml.cs
public MainWindow()
{
    InitializeComponent();
    Loaded += async (_, _) =>
    {
        await WebView.NavigateAsync(new Uri("https://example.com"));
    };
}
```

## First Typed Bridge Contract

Define contracts once, then call across C# and JavaScript with type safety.

```csharp
[JsExport] // C# -> JS
public interface IGreeterService
{
    Task<string> Greet(string name);
}

[JsImport] // JS -> C#
public interface INotificationService
{
    Task ShowNotification(string message);
}
```

Expose C# service:

```csharp
WebView.Bridge.Expose<IGreeterService>(new GreeterServiceImpl());
```

Call from JavaScript:

```javascript
const result = await window.agWebView.rpc.invoke("GreeterService.greet", { name: "World" });
console.log(result);
```

Call JavaScript from C#:

```csharp
var notifier = WebView.Bridge.GetProxy<INotificationService>();
await notifier.ShowNotification("Hello from C#!");
```

## First Web-First SPA Hosting Setup

Production mode (embedded assets):

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    EmbeddedResourcePrefix = "wwwroot",
    ResourceAssembly = typeof(MainWindow).Assembly,
});

await WebView.NavigateAsync(new Uri("app://localhost/index.html"));
```

Development mode (HMR proxy):

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    DevServerUrl = "http://localhost:5173",
});
```

## Next Steps

- [Architecture](architecture.md) — Runtime topology and design invariants
- [Bridge Guide](bridge-guide.md) — Advanced bridge patterns
- [SPA Hosting](spa-hosting.md) — Detailed hosting configuration
- [Plugin Authoring Guide](../plugin-authoring-guide.md) — Create and consume bridge plugins
- [CLI Reference](../cli.md) — `fulora new`, `dev`, `generate`, `search`, `add plugin`
- [Demo walkthrough](../demo/index.md) — End-to-end sample experience
- [Roadmap](../../openspec/ROADMAP.md) — Product direction and milestones
