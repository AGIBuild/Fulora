# Release Checklist

Step-by-step guide for publishing a new Agibuild.Fulora release via the unified CI/Release pipeline.

## Normative Reference

- [Release Governance](release-governance.md) — authoritative stable release rules, gate definitions, and promotion constraints for this checklist.

## How Releases Work

Releases are fully automated through the unified `ci.yml` workflow:

1. **Version baseline** is defined in `Directory.Build.props` (`<VersionPrefix>`). Update it with `nuke UpdateVersion` when you want to bump the version.
2. **Every push to `main`** triggers the CI pipeline, which builds and tests across macOS, Windows, and Linux.
3. **Package version** is computed as `{VersionPrefix}.{run_number}` — deterministic, monotonic, and no manual tagging required.
4. **After CI passes**, the Release Promotion job waits for **manual approval** via the `release` GitHub environment.
5. **After approval**, the pipeline publishes NuGet packages, npm packages, creates a Git tag (`v{version}`), a GitHub Release, and deploys documentation — all in one run.

## Prerequisites

### GitHub Secrets

| Secret | Description |
|--------|-------------|
| `NUGET_API_KEY` | NuGet.org API key with push permissions for `Agibuild.Fulora.*` packages |
| `NPM_TOKEN` | npm access token with publish permissions for `@agibuild` scope |

### GitHub Environment

The `release` environment must have **required reviewers** configured (Settings → Environments → release → Required reviewers).

## Version Management

```bash
# Auto-increment patch (e.g. 1.5.0 → 1.5.1)
./build.sh --target UpdateVersion

# Set explicit version (must be greater than current)
./build.sh --target UpdateVersion --update-version-to 2.0.0
```

After updating the version, commit the `Directory.Build.props` change and push to `main`. The next CI run will produce packages with the new version baseline.

## Local Verification (before pushing)

```bash
# Run full CI pipeline locally
./build.sh --target Ci --configuration Release --package-version 1.5.0.999

# Or run individual steps
./build.sh --target Coverage       # Unit tests + coverage
./build.sh --target ValidatePackage # Pack + validate NuGet contents
```

## Monitoring a Release

1. Push to `main` → CI runs automatically
2. Watch the [Actions tab](https://github.com/AGIBuild/Fulora/actions) for the "CI and Release" workflow
3. After three platforms pass, click **Review deployments** to approve the release
4. Release Promotion publishes packages, creates tag + GitHub Release
5. Deploy Documentation builds and deploys the docfx site

## Post-Release Verification

```bash
# Verify NuGet packages
dotnet package search Agibuild.Fulora.Avalonia --exact-match

# Verify npm package
npm info @agibuild/bridge

# Verify GitHub Release
gh release list --limit 3
```

## Published Packages

### NuGet Packages

| Package | Description |
|---|---|
| `Agibuild.Fulora.Core` | Core contracts and interfaces |
| `Agibuild.Fulora.Runtime` | Runtime implementation |
| `Agibuild.Fulora.Avalonia` | Avalonia WebView control |
| `Agibuild.Fulora.Bridge.Generator` | Roslyn source generator |
| `Agibuild.Fulora.Cli` | CLI tool (`fulora new`, `dev`, `generate`) |
| `Agibuild.Fulora.Telemetry.OpenTelemetry` | OpenTelemetry bridge tracer |
| `Agibuild.Fulora.Plugin.*` | Official bridge plugins (Database, HttpClient, FileSystem, Notifications, AuthToken, LocalStorage) |

### npm Packages

| Package | Description |
|---|---|
| `@agibuild/bridge` | Bridge client, HMR preservation, Web Worker relay |

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| NuGet push fails with 409 | Package already exists. `--skip-duplicate` handles this automatically. |
| npm publish fails with 401 | `NPM_TOKEN` is missing or expired. Regenerate and update the GitHub secret. |
| Release job runs without approval | Verify `release` environment has required reviewers configured. |
| Docs not deploying | Check GitHub Pages source is set to "GitHub Actions" in repo settings. |
