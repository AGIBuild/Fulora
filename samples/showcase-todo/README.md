# Showcase Todo App

A minimal hybrid desktop app demonstrating the Agibuild.Fulora bridge: an Avalonia WebView hosting a React SPA that communicates with C# via `[JsExport]` interfaces.

## Structure

- **ShowcaseTodo.Desktop** — Avalonia desktop app with WebView and SPA hosting
- **ShowcaseTodo.Bridge** — Bridge interfaces (`ITodoService`) and implementation (`TodoService`)
- **ShowcaseTodo.Web** — React (Vite) frontend using `@agibuild/fulora-client` to call C# services

## Run

1. **Debug** (Vite dev server):
   ```bash
   cd ShowcaseTodo.Web && npm install && npm run dev
   ```
   In another terminal:
   ```bash
   dotnet run --project samples/showcase-todo/ShowcaseTodo.Desktop/ShowcaseTodo.Desktop.csproj
   ```

2. **Release** (embedded SPA):
   ```bash
   cd ShowcaseTodo.Web && npm install && npm run build
   dotnet run -c Release --project samples/showcase-todo/ShowcaseTodo.Desktop/ShowcaseTodo.Desktop.csproj
   ```

## Features

- Add, toggle, and delete todos via the bridge
- In-memory `TodoService` implementation
- Standalone mock in `index.html` for browser preview without the host
