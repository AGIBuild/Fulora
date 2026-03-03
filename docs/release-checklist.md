# Release Checklist

Step-by-step guide for publishing a new Agibuild.Fulora release (NuGet + npm + GitHub Release).

## Prerequisites

### GitHub Secrets Configuration

| Secret | Environment | Description |
|--------|-------------|-------------|
| `NUGET_API_KEY` | `nuget` | NuGet.org API key with push permissions for `Agibuild.Fulora.*` packages |
| `NPM_TOKEN` | `npm` | npm access token with publish permissions for `@agibuild` scope |

### Local Verification

Before creating a release tag, verify locally:

```bash
# Run full test suite
nuke Test

# Run coverage (line ≥ 96%, branch ≥ 95%)
nuke Coverage

# Pack and validate NuGet packages
nuke ValidatePackage

# Build npm bridge package
cd packages/bridge && npm ci && npm run build
```

## Creating a Release

### 1. Decide the version

- **Stable**: `v1.0.0`, `v1.1.0`, `v2.0.0` (semver, no suffix)
- **Pre-release**: `v1.1.0-preview`, `v2.0.0-rc.1` (semver with suffix)

MinVer derives the NuGet version from the git tag automatically.
The npm version is extracted from the tag in CI and set before publish.

### 2. Create and push the tag

```bash
git tag v1.0.0
git push origin v1.0.0
```

### 3. Monitor the release workflow

The `Release` workflow (`.github/workflows/release.yml`) triggers automatically:

1. **Build job** (macOS): Compiles, runs tests, validates packages
2. **Publish job** (parallel): Pushes `.nupkg` to NuGet.org
3. **npm-publish job** (parallel): Publishes `@agibuild/bridge` to npm
4. **GitHub Release job** (parallel): Creates a GitHub Release with auto-generated notes

### 4. Post-publish verification

```bash
# Verify core NuGet packages
dotnet package search Agibuild.Fulora.Avalonia --exact-match

# Verify plugin NuGet packages
dotnet package search Agibuild.Fulora.Plugin.Database --exact-match
dotnet package search Agibuild.Fulora.Plugin.HttpClient --exact-match
dotnet package search Agibuild.Fulora.Plugin.FileSystem --exact-match
dotnet package search Agibuild.Fulora.Plugin.Notifications --exact-match
dotnet package search Agibuild.Fulora.Plugin.AuthToken --exact-match

# Verify OpenTelemetry package
dotnet package search Agibuild.Fulora.Telemetry.OpenTelemetry --exact-match

# Verify npm packages
npm info @agibuild/bridge
npm info @agibuild/bridge-plugin-database
npm info @agibuild/bridge-plugin-http-client
npm info @agibuild/bridge-plugin-file-system
npm info @agibuild/bridge-plugin-notifications
npm info @agibuild/bridge-plugin-auth-token

# Verify GitHub Release
gh release view v1.0.0
```

## Published Packages

### NuGet Packages

| Package | Description |
|---|---|
| `Agibuild.Fulora.Core` | Core contracts and interfaces |
| `Agibuild.Fulora.Runtime` | Runtime implementation |
| `Agibuild.Fulora.Avalonia` | Avalonia WebView control |
| `Agibuild.Fulora.Bridge.Generator` | Roslyn source generator |
| `Agibuild.Fulora.DependencyInjection` | DI integration (`AddFulora()`) |
| `Agibuild.Fulora.Telemetry.OpenTelemetry` | OpenTelemetry bridge tracer + telemetry provider |
| `Agibuild.Fulora.Plugin.LocalStorage` | Key-value local storage plugin |
| `Agibuild.Fulora.Plugin.Database` | SQLite database plugin |
| `Agibuild.Fulora.Plugin.HttpClient` | Host-routed HTTP client plugin |
| `Agibuild.Fulora.Plugin.FileSystem` | Sandboxed file system plugin |
| `Agibuild.Fulora.Plugin.Notifications` | System notifications plugin |
| `Agibuild.Fulora.Plugin.AuthToken` | Secure token storage plugin |

### npm Packages

| Package | Description |
|---|---|
| `@agibuild/bridge` | Bridge client, HMR preservation, Web Worker relay |
| `@agibuild/bridge-plugin-local-storage` | LocalStorage typed client |
| `@agibuild/bridge-plugin-database` | Database typed client |
| `@agibuild/bridge-plugin-http-client` | HTTP client typed client |
| `@agibuild/bridge-plugin-file-system` | File system typed client |
| `@agibuild/bridge-plugin-notifications` | Notifications typed client |
| `@agibuild/bridge-plugin-auth-token` | Auth token typed client |

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| NuGet push fails with 409 | Package already exists at that version. `--skip-duplicate` handles this. |
| npm publish fails with 409 | Version already published. The `\|\| true` fallback prevents workflow failure. |
| npm publish fails with 401 | `NPM_TOKEN` is missing or expired. Regenerate token and update the GitHub secret. |
| Build job fails | Check test results artifact. Fix issues and re-tag (delete old tag first if needed). |
