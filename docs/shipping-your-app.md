# Shipping Your Fulora App

This guide covers packaging, code signing, auto-update setup, and CI integration for Fulora hybrid desktop applications.

## Prerequisites

- **.NET SDK** (net10.0 or later)
- **Velopack CLI (`vpk`)** — for creating installers and update packages
  - Install: `dotnet tool install -g vpk` or download from [Velopack releases](https://github.com/velopack/velopack/releases)
  - Verify: `vpk -H`

## Basic Packaging Workflow

### 1. Publish and package

```bash
fulora package --project ./src/MyApp.Desktop/MyApp.Desktop.csproj \
  --runtime win-x64 \
  --version 1.0.0 \
  --output ./Releases
```

### 2. Options

| Option | Description | Default |
|--------|-------------|---------|
| `--project`, `-p` | Path to the .csproj (required) | — |
| `--runtime`, `-r` | Target RID (win-x64, osx-arm64, linux-x64, etc.) | win-x64 |
| `--version`, `-v` | Package version (semver) | From &lt;Version&gt; in .csproj |
| `--output`, `-o` | Output directory | ./Releases |
| `--icon`, `-i` | Path to icon file | — |
| `--sign-params`, `-n` | Code signing parameters (platform-specific) | — |
| `--notarize` | Enable macOS notarization (uses notaryProfile default) | false |
| `--channel`, `-c` | Release channel | stable |

### 3. What gets produced

- **Windows**: `MyApp-Setup.exe`, `MyApp-Portable.zip`, `*.nupkg` (full + delta)
- **macOS**: `.pkg` installer, `.dmg`
- **Linux**: `.AppImage`

If `vpk` is not installed, `fulora package` falls back to copying the `dotnet publish` output into the output directory.

## Code Signing Notes

### Windows

Use `--sign-params` to pass arguments to `signtool.exe`:

```bash
fulora package --project MyApp.csproj --sign-params "/a /tr http://timestamp.digicert.com /td sha256 /fd sha256"
```

### macOS

Signing and notarization require:

1. **Signing identity** in Keychain (Developer ID Application)
2. **Notary credentials** (Apple ID + app-specific password) stored via `xcrun notarytool store-credentials`

```bash
fulora package --project MyApp.csproj --runtime osx-arm64 --notarize
```

For custom signing, use `vpk pack` directly with `--signAppIdentity` and `--signInstallIdentity`.

## Auto-Update Setup

### 1. Register VelopackAutoUpdateProvider

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
            --runtime win-x64 \
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
