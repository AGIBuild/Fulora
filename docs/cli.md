# Fulora CLI

The `fulora` CLI is a .NET global tool for the main Fulora app-building path: create a project, run it locally, and package it for distribution.

## Installation

```bash
dotnet tool install -g Agibuild.Fulora.Cli
```

## Primary Path

Start here if you are building an app.

### `fulora new <name>`

Create a new Fulora hybrid app from the template.

```bash
fulora new MyApp --frontend react
fulora new MyApp --frontend vue --shell-preset app-shell
```

| Option | Description |
|---|---|
| `--frontend`, `-f` | **Required.** `react`, `vue`, or `vanilla` |
| `--shell-preset` | Desktop shell preset: `baseline` or `app-shell` |

Generated React/Vue apps now include a modern frontend script baseline:

```bash
npm run dev:mock
npm run test
npm run test:browser
npm run test:e2e
npm run check
```

Use `dev:mock` when you want to work on the frontend without launching the desktop host. Use `test:browser` and `test:e2e` for browser-mode and Playwright smoke coverage that match the shipped template defaults.

### `fulora attach web`

Attach an existing web app to a Fulora desktop host without rewriting the frontend first.

```bash
fulora attach web --web ./app/web --desktop ./app/desktop --bridge ./app/bridge --framework react
fulora attach web --web ./app/web --framework vue --web-command "npm run dev" --dev-server-url http://localhost:5173
```

This is the canonical brownfield path:

```text
attach web -> dev -> package
```

The command creates Fulora-owned wiring, writes `fulora.json`, and leaves your existing frontend business code intact.

| Option | Description |
|---|---|
| `--web` | **Required.** Existing web project directory containing `package.json` |
| `--desktop` | Desktop project directory to create/reuse |
| `--bridge` | Bridge project directory to create/reuse |
| `--framework` | `react`, `vue`, or `generic` |
| `--web-command` | Existing frontend dev command to echo in next steps |
| `--dev-server-url` | Existing frontend dev server URL such as `http://localhost:5173` |

`fulora attach web` establishes the structure once. If generated bridge artifacts later drift, regenerate them explicitly with `fulora generate types`; Fulora reports drift in preflight checks instead of silently auto-fixing generated files.

### `fulora dev`

Start the Vite dev server and Avalonia desktop app together. Run from the solution root.

```bash
fulora dev
fulora dev --web ./MyApp.Web.Vite.React --desktop ./MyApp.Desktop/MyApp.Desktop.csproj
fulora dev --preflight-only
```

| Option | Description |
|---|---|
| `--web` | Web project directory (auto-detected) |
| `--desktop` | Desktop `.csproj` path (auto-detected) |
| `--npm-script` | npm script name (default: `dev`) |
| `--preflight-only` | Run bridge/dev preflight checks and exit without starting the dev processes |

Press **Ctrl+C** to stop both processes.

Use `--preflight-only` when you want to validate bridge artifact consistency and project wiring without launching Vite or the desktop host.

When `fulora.json` is present, `fulora dev` reuses the attached brownfield workspace configuration before falling back to heuristics.

### `fulora package`

Package your app for distribution. The recommended first path is to start with a named profile.

```bash
fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj --profile desktop-public
fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj --profile mac-notarized
fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj --profile desktop-public --preflight-only
```

Available profiles today:

- `desktop-public`
- `desktop-internal`
- `mac-notarized`

| Option | Description |
|---|---|
| `--profile` | Packaging profile with recommended defaults |
| `--project`, `-p` | Path to the `.csproj` (required) |
| `--runtime`, `-r` | Target RID such as `win-x64`, `osx-arm64`, or `linux-x64` |
| `--version`, `-v` | Package version (semver). Defaults to the project version |
| `--output`, `-o` | Output directory. Defaults to `./Releases` under the project |
| `--icon`, `-i` | Path to the app icon |
| `--sign-params`, `-n` | Raw signing parameters passed to `vpk` |
| `--notarize` | Enable macOS notarization |
| `--channel`, `-c` | Release channel |
| `--preflight-only` | Run packaging and bridge consistency preflight checks, then exit without publishing or packing |

If `vpk` is not installed, `fulora package` falls back to copying the `dotnet publish` output into the output directory.

The command now emits **preflight notes** before packaging when the selected profile implies extra setup. Examples:

- `desktop-public` without `vpk` → warns that Fulora will copy publish output instead of producing installer/update packages
- `mac-notarized` without `vpk` → warns that the fallback output will not be notarized
- `mac-notarized` on a non-macOS host → warns that notarization usually needs a macOS host

Use `--preflight-only` when you want those checks without triggering `dotnet publish` or `vpk`.

When `fulora.json` is present, `fulora package` reuses the configured desktop project before falling back to heuristics.

## Advanced Workflows

Use these commands after the main path is already working.

### `fulora generate types`

Build the Bridge project and extract the generated bridge artifacts:

- `bridge.d.ts`
- `bridge.client.ts`
- `bridge.mock.ts`
- `bridge.manifest.json`

```bash
fulora generate types
fulora gen types --project ./MyApp.Bridge/MyApp.Bridge.csproj --output ./MyApp.Web/src/bridge
```

| Option | Description |
|---|---|
| `--project`, `-p` | Bridge `.csproj` path (auto-detected) |
| `--output`, `-o` | Output directory for generated bridge artifacts (auto-detected; prefers `src/bridge/generated` when present) |

The generated `bridge.manifest.json` records the bridge project identity, artifact directory, build configuration, target framework, bridge assembly hash, and artifact hashes so `fulora dev` and `fulora package` can detect missing or stale generated outputs without relying only on timestamps.

Use this command explicitly when bridge artifacts drift. Fulora surfaces the mismatch in preflight output; it does not silently auto-fix generated files during `dev` or `package`.

### `fulora add service <name>`

Scaffold a new bridge service with three files:

1. C# interface (`[JsExport]`) in the Bridge project
2. C# implementation in the Desktop project
3. TypeScript proxy in the web project

```bash
fulora add service NotificationService --layer bridge
fulora add service IAnalyticsService --layer plugin --import
```

| Option | Description |
|---|---|
| `--layer` | **Required.** Service ownership layer: `bridge`, `framework`, or `plugin` |
| `--import` | Generate `[JsImport]` instead of `[JsExport]` |
| `--bridge-project` | Bridge project path (auto-detected) |
| `--web-dir` | Web project `src/` directory (auto-detected) |

### `fulora add plugin <package>`

Install a Fulora bridge plugin NuGet package into the current project.

```bash
fulora add plugin Agibuild.Fulora.Plugin.Database
fulora add plugin Agibuild.Fulora.Plugin.HttpClient --project ./MyApp.Desktop/MyApp.Desktop.csproj
```

| Option | Description |
|---|---|
| `--project`, `-p` | Path to the `.csproj` file (auto-detected if omitted) |

### `fulora search [query]`

Search NuGet.org for Fulora bridge plugins tagged `fulora-plugin`.

```bash
fulora search database
fulora search http --take 20
```

| Option | Description |
|---|---|
| `--take` | Maximum results to return (default: 20) |

### `fulora list plugins`

List the Fulora plugin packages installed in the current project.

```bash
fulora list plugins
fulora list plugins --check
```

| Option | Description |
|---|---|
| `--check` | Check plugin compatibility with the installed Fulora version |
