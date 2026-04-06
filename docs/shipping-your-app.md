# Shipping Your Fulora App

This guide covers packaging, code signing, auto-update setup, and CI integration for Fulora hybrid desktop applications.

## Prerequisites

- **.NET SDK** (`net10.0` or later)
- **Velopack CLI (`vpk`)** for installers and update packages
  - Install: `dotnet tool install -g vpk` or download from [Velopack releases](https://github.com/velopack/velopack/releases)
  - Verify: `vpk -H`

## Basic Packaging Workflow

### 1. Start with a packaging profile

```bash
fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj \
  --profile desktop-public \
  --version 1.0.0 \
  --output ./Releases

fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj \
  --profile desktop-public \
  --preflight-only
```

Profiles are the productized shipping path in Fulora. They bundle the recommended defaults for a release scenario so you do not need to remember low-level flags every time.

Current built-in profiles:

- `desktop-public` for a normal public desktop release
- `desktop-internal` for internal builds on the `internal` channel
- `mac-notarized` for macOS releases that should default to notarization

You can still override profile defaults with explicit flags. For example, `--runtime linux-x64` or `--channel preview` wins over the profile setting.

If you want to verify packaging prerequisites without running publish/pack yet, use `--preflight-only`.

### 2. Package command options

| Option | Description | Default |
|--------|-------------|---------|
| `--profile` | Packaging profile with recommended defaults | — |
| `--project`, `-p` | Path to the `.csproj` (required) | — |
| `--runtime`, `-r` | Target RID (`win-x64`, `osx-arm64`, `linux-x64`, etc.) | `win-x64` |
| `--version`, `-v` | Package version (semver) | From `<Version>` in the `.csproj` |
| `--output`, `-o` | Output directory | `./Releases` under the project |
| `--icon`, `-i` | Path to icon file | — |
| `--sign-params`, `-n` | Raw code signing parameters passed to `vpk` | — |
| `--notarize` | Enable macOS notarization | `false` unless the profile enables it |
| `--channel`, `-c` | Release channel | `stable` unless the profile changes it |

### 3. What gets produced

- **Windows**: `MyApp-Setup.exe`, `MyApp-Portable.zip`, `*.nupkg` (full + delta)
- **macOS**: `.pkg` installer, `.dmg`
- **Linux**: `.AppImage`

If `vpk` is not installed, `fulora package` falls back to copying the `dotnet publish` output into the output directory.

Fulora now prints **preflight notes** before packaging when the chosen profile implies extra setup. Typical examples:

- `desktop-public` without `vpk`: you will get copied publish output, not installer/update packages
- `mac-notarized` without `vpk`: the fallback output will not be notarized
- `mac-notarized` on a non-macOS host: you may need to finish signing/notarization on macOS

`fulora package` also checks the bridge artifact manifest (`bridge.manifest.json`) when it can locate a sibling Bridge project. That lets it warn about missing or stale generated bridge files before packaging continues.

## Raw Signing And Notarization Flags

Most teams should stay on the profile-based path. Use the raw flags below when you need to tune signing behavior for a specific environment.

### Windows signing

Use `--sign-params` to pass arguments through to `signtool.exe` via `vpk`:

```bash
fulora package --project MyApp.csproj \
  --profile desktop-public \
  --sign-params "/a /tr http://timestamp.digicert.com /td sha256 /fd sha256"
```

### macOS notarization

Signing and notarization require:

1. A signing identity in Keychain (`Developer ID Application`)
2. Notary credentials stored with `xcrun notarytool store-credentials`

If you want the Fulora default notarized path, use the profile:

```bash
fulora package --project MyApp.csproj --profile mac-notarized
```

You can also opt in manually with the raw flag:

```bash
fulora package --project MyApp.csproj --runtime osx-arm64 --notarize
```

For custom signing identities beyond the built-in path, use `vpk pack` directly with `--signAppIdentity` and `--signInstallIdentity`.

## Auto-Update Setup

### 1. Register `VelopackAutoUpdateProvider`

```csharp
builder.AddAutoUpdate(
    new AutoUpdateOptions
    {
        FeedUrl = "https://releases.myapp.com/releases.stable.json",
        CheckInterval = TimeSpan.FromHours(1),
        AutoDownload = true,
    },
    new VelopackAutoUpdateProvider());
```

### 2. Feed URL format

Point `FeedUrl` to a JSON feed. Supported formats:

**Velopack-style** (`releases.{channel}.json`):

```json
{
  "Assets": [
    {
      "PackageId": "MyApp",
      "Version": "1.0.1",
      "Type": "Full",
      "FileName": "MyApp-1.0.1-full.nupkg",
      "SHA256": "abc123...",
      "Size": 1588612
    }
  ]
}
```

**Simple format**:

```json
{
  "version": "1.0.1",
  "downloadUrl": "https://releases.myapp.com/MyApp-1.0.1-full.nupkg",
  "sha256": "abc123...",
  "sizeBytes": 1588612,
  "releaseNotes": "Bug fixes",
  "isMandatory": false
}
```

### 3. Hosting

Host the feed and packages on any static file server:

- GitHub Releases
- S3 / Azure Blob
- Custom server

Ensure the feed URL is publicly accessible and CORS allows the app to fetch it.

## CI Integration Example

### GitHub Actions

```yaml
# .github/workflows/release.yml
jobs:
  package:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Install vpk
        run: dotnet tool install -g vpk
      - name: Package
        run: |
          dotnet tool install -g Agibuild.Fulora.Cli
          fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj \
            --profile desktop-public \
            --version ${{ github.ref_name }} \
            --output ./Releases
      - name: Upload artifacts
        uses: actions/upload-artifact@v4
        with:
          name: releases
          path: ./Releases
```

### Nuke build script

```csharp
Target Package => _ => _
    .Executes(() =>
    {
        var project = Solution.GetProject("MyApp.Desktop");
        var version = project.GetVersion();
        VpkTasks.VpkPack(s => s
            .SetPackId("MyApp")
            .SetPackVersion(version)
            .SetPackDir(project.Directory / "bin" / "Release" / "net10.0" / "win-x64" / "publish")
            .SetMainExe("MyApp.Desktop.exe")
            .SetOutputDir(OutputDir));
    });
```

## See Also

- [Velopack documentation](https://docs.velopack.io/)
- [Release Governance](release-governance.md)
