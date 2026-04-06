# Getting Started

Get a hybrid desktop app running in under five minutes — a native Avalonia window hosting your web frontend so you can start building product features right away.

## The main path

For most teams, the fastest route is:

1. Install the Fulora CLI and project template
2. Run `fulora new` to scaffold a new app
3. Run `fulora dev` to start the frontend and native shell together

That path gives you a working app first. The bridge details are still important, but you do not need to learn them before you can get started.

## Prerequisites

- .NET 10 SDK
- Platform WebView runtime:
  - **Windows**: WebView2 (ships with Edge)
  - **macOS/iOS**: WKWebView (built-in)
  - **Android**: Android WebView (built-in)
  - **Linux**: WebKitGTK (`libwebkit2gtk-4.1`)

## 1. Install the CLI and template

Install the Fulora CLI and template once on your machine:

```bash
dotnet tool install -g Agibuild.Fulora.Cli
dotnet new install Agibuild.Fulora.Templates
```

## 2. Run `fulora new`

Create a new app from the template:

```bash
fulora new MyApp --frontend react
cd MyApp
```

The template gives you a native host, a web frontend, and the default services already wired together.

## 3. Run `fulora dev`

Start the frontend dev server and the Avalonia shell together:

```bash
fulora dev
```

You'll see a native window open with your app inside, ready for normal product work.

After these three steps, you have:

- A native desktop window hosting your web app
- A web frontend and native host already wired together
- Fulora services ready for normal app development

Alternatively, use `dotnet new` directly:

```bash
dotnet new agibuild-hybrid -n MyApp
cd MyApp
dotnet run --project MyApp.Desktop
```

## Fulora services already use the bridge underneath

When you use `fulora new`, you mostly work with app-level services and UI code first. Under the hood, those services talk across the Fulora bridge for you.

- Your frontend still runs inside a native WebView
- Your native code still runs in-process in C#
- Fulora services use generated bridge contracts underneath, so app code can stay focused on product features

This means you can start by building screens and calling services, then learn the bridge layer when you need custom native capabilities or plugin work.

If you want the mental model, it looks like this:

```
┌───────────────────────────────────┐
│  Native Avalonia Window           │
│  ┌─────────────────────────────┐  │
│  │  React SPA in WebView       │  │
│  │                             │  │
│  │  await GreeterService       │  │
│  │    .greet("World")          │  │
│  └──────────┬──────────────────┘  │
│             │ type-safe bridge     │
│  ┌──────────▼──────────────────┐  │
│  │  C# GreeterServiceImpl     │  │
│  │  → "Hello, World!"         │  │
│  └─────────────────────────────┘  │
└───────────────────────────────────┘
```

## Manual path: add Fulora to an existing Avalonia app

If you already have an Avalonia project or need full control over the setup.

### 1. Add the NuGet package

```bash
dotnet add package Agibuild.Fulora.Avalonia
```

### 2. Add the WebView control to your window

```xml
<!-- MainWindow.axaml -->
<Window xmlns:wv="clr-namespace:Agibuild.Fulora;assembly=Agibuild.Fulora">
    <wv:WebView x:Name="WebView" />
</Window>
```

### 3. Navigate to a page

```csharp
public MainWindow()
{
    InitializeComponent();
    Loaded += async (_, _) =>
    {
        await WebView.NavigateAsync(new Uri("https://example.com"));
    };
}
```

## Advanced: bridge details

Once your app is running, you can go deeper into the bridge model. Define a C# interface, and the source generator creates everything needed for type-safe cross-language calls — no serialization boilerplate, no runtime reflection.

```csharp
[JsExport] // C# implementation, callable from JavaScript
public interface IGreeterService
{
    Task<string> Greet(string name);
}

[JsImport] // JavaScript implementation, callable from C#
public interface INotificationService
{
    Task ShowNotification(string message);
}
```

Expose your C# service to the web frontend:

```csharp
WebView.Bridge.Expose<IGreeterService>(new GreeterServiceImpl());
```

Call it from JavaScript — the bridge client is auto-generated:

```javascript
import { services } from "./bridge/client";

const result = await services.greeter.greet({ name: "World" });
// → "Hello, World!"
```

Call JavaScript from C# with the same type safety:

```csharp
var notifier = WebView.Bridge.GetProxy<INotificationService>();
await notifier.ShowNotification("Hello from C#!");
```

## SPA hosting details

Fulora can serve your web frontend from embedded resources (production) or proxy to a dev server (development).

**Production** — embedded assets with `app://` scheme:

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    EmbeddedResourcePrefix = "wwwroot",
    ResourceAssembly = typeof(MainWindow).Assembly,
});

await WebView.NavigateAsync(new Uri("app://localhost/index.html"));
```

**Development** — Vite/Webpack dev server with HMR:

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    DevServerUrl = "http://localhost:5173",
});
```

## Next Steps

Now that you have a running app, dive deeper:

- [Bridge Guide](bridge-guide.md) — Advanced patterns: streaming, cancellation, error handling, batch calls
- [SPA Hosting](spa-hosting.md) — Production hosting, dev server proxy, HMR state preservation
- [Architecture](architecture.md) — How the runtime, policy engine, and capability gateway work together
- [Plugin Authoring](../plugin-authoring-guide.md) — Create bridge plugins that ship as NuGet + npm
- [CLI Reference](../cli.md) — `fulora new`, `dev`, `generate types`, `add service`, `search`
- [Demo Walkthrough](../demo/index.md) — A full-featured sample with Dashboard, Chat, Files, and Settings
