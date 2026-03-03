# Fulora CLI

The `fulora` CLI is a .NET global tool that streamlines common Fulora hybrid app workflows.

## Installation

```bash
dotnet tool install -g Agibuild.Fulora.Cli
```

## Commands

### `fulora new <name>`

Create a new Fulora hybrid app from the template.

```bash
fulora new MyApp --frontend react
fulora new MyApp --frontend vue --preset app-shell
```

| Option | Description |
|---|---|
| `--frontend`, `-f` | **Required.** `react`, `vue`, or `vanilla` |
| `--preset` | Template preset (e.g. `app-shell`) |

### `fulora dev`

Start Vite dev server and Avalonia desktop app together. Run from the solution root.

```bash
fulora dev
fulora dev --web ./MyApp.Web.Vite.React --desktop ./MyApp.Desktop/MyApp.Desktop.csproj
```

| Option | Description |
|---|---|
| `--web` | Web project directory (auto-detected) |
| `--desktop` | Desktop .csproj path (auto-detected) |
| `--npm-script` | npm script name (default: `dev`) |

Press **Ctrl+C** to stop both processes.

### `fulora generate types`

Build the Bridge project and extract generated TypeScript declarations.

```bash
fulora generate types
fulora gen types --project ./MyApp.Bridge/MyApp.Bridge.csproj --output ./MyApp.Web/src/bridge
```

| Option | Description |
|---|---|
| `--project`, `-p` | Bridge .csproj path (auto-detected) |
| `--output`, `-o` | Output directory for `.d.ts` (auto-detected) |

### `fulora add service <name>`

Scaffold a new bridge service with three files:
1. C# interface (`[JsExport]`) in the Bridge project
2. C# implementation in the Desktop project
3. TypeScript proxy in the web project

```bash
fulora add service NotificationService
fulora add service IAnalyticsService --import
```

| Option | Description |
|---|---|
| `--import` | Generate `[JsImport]` instead of `[JsExport]` |
| `--bridge-project` | Bridge project path (auto-detected) |
| `--web-dir` | Web project `src/` directory (auto-detected) |

### `fulora add plugin <package>`

Install a Fulora bridge plugin NuGet package into the current project.

```bash
fulora add plugin Agibuild.Fulora.Plugin.Database
fulora add plugin Agibuild.Fulora.Plugin.HttpClient --version 1.0.0
```

| Option | Description |
|---|---|
| `--version` | Specific package version (default: latest) |

### `fulora search <query>`

Search NuGet.org for Fulora bridge plugins (packages tagged `fulora-plugin`).

```bash
fulora search database
fulora search http --take 20
```

| Option | Description |
|---|---|
| `--take` | Maximum results to return (default: 10) |

### `fulora list plugins`

List all installed Fulora plugin packages in the current project.

```bash
fulora list plugins
fulora list plugins --project ./MyApp.Desktop/MyApp.Desktop.csproj
```

| Option | Description |
|---|---|
| `--project` | Project file to inspect (auto-detected) |
