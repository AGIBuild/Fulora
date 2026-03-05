## Context

Fulora's build pipeline produces NuGet packages and npm packages via Nuke build. The `IAutoUpdateService` and `IAutoUpdatePlatformProvider` interfaces exist but have no concrete implementation. Developers must manually handle `dotnet publish`, installer creation, code signing, and auto-update wiring. Velopack (MIT, Rust-based, cross-platform) is the leading .NET desktop app packaging tool — successor to Squirrel.Windows.

**Existing infrastructure:**
- `IAutoUpdatePlatformProvider`: `CheckForUpdateAsync`, `DownloadUpdateAsync`, `VerifyPackageAsync`, `ApplyUpdateAsync`, `GetCurrentVersion`
- `AutoUpdateOptions`: `FeedUrl`, `CheckInterval`, `AutoDownload`, `Headers`
- `UpdateInfo`: `Version`, `ReleaseNotes`, `DownloadUrl`, `SizeBytes`, `IsMandatory`, `Sha256`
- CLI: `fulora new/dev/generate/add/search/list-plugins`

## Goals / Non-Goals

**Goals:**
- `fulora package` CLI command: `dotnet publish` → Velopack `vpk pack` → installers + feed
- `VelopackAutoUpdateProvider : IAutoUpdatePlatformProvider` wired via `AddAutoUpdate()`
- Support: Windows (Setup.exe + NuGet delta), macOS (DMG/pkg), Linux (AppImage)
- Code signing: `--sign-params` passthrough to Velopack
- macOS notarization: `--notarize` flag
- Release feed: `releases.{channel}.json` hosted on any static file server
- Documentation: `docs/shipping-your-app.md`

**Non-Goals:**
- App store submission (MS Store, Mac App Store)
- Mobile packaging (APK/IPA)
- Custom installer UI
- Hosting an update server (use GitHub Releases, S3, Azure Blob)

## Decisions

### D1: Velopack as packaging backend

**Choice**: Use Velopack CLI (`vpk`) as the packaging engine. Fulora's `fulora package` command wraps it with sensible defaults.

**Rationale**: Velopack is MIT-licensed, cross-platform, actively maintained, produces delta updates, and is the de facto standard for .NET desktop packaging. Avoids building a custom packaging engine.

### D2: VelopackAutoUpdateProvider wraps Velopack SDK

**Choice**: `VelopackAutoUpdateProvider` uses `Velopack.UpdateManager` internally to implement `IAutoUpdatePlatformProvider`.

**Rationale**: Velopack SDK handles platform-specific update logic (Windows: Squirrel-style, macOS: Sparkle-style, Linux: AppImage replacement). Fulora just maps to the existing interface.

### D3: fulora package command design

**Choice**:
```bash
fulora package \
  --project <path.csproj> \
  --runtime <rid>           # win-x64, osx-arm64, linux-x64
  --version <semver> \
  --output <dir> \
  --icon <path> \
  --sign-params <params> \  # platform-specific signing
  --notarize \              # macOS only
  --channel <channel>       # default: stable
```

Internally:
1. `dotnet publish -c Release -r <rid> --self-contained -o <temp>/publish`
2. `vpk pack --packId <name> --packVersion <version> --packDir <temp>/publish --mainExe <exe> --outputDir <output>`

**Rationale**: Minimal wrapper over proven tools. Developers who need advanced Velopack options can use `vpk` directly.

### D4: Nuke integration for CI

**Choice**: Add `PackageApp` nuke target that calls `fulora package` or invokes `vpk` directly from the build script.

**Rationale**: CI pipelines need a build-system-integrated target. Nuke's `Tool` support can invoke external commands.

### D5: Auto-update DI registration

**Choice**:
```csharp
services.AddFulora(builder => {
    builder.AddAutoUpdate(options => {
        options.FeedUrl = "https://releases.myapp.com";
        options.UseVelopack(); // registers VelopackAutoUpdateProvider
    });
});
```

**Rationale**: Follows existing `AddFulora()` builder pattern. `UseVelopack()` is explicit opt-in.

## Risks / Trade-offs

- **[Risk] Velopack breaking changes** → Pin to major version; Velopack is post-1.0 and stable.
- **[Risk] Code signing complexity** → Passthrough to Velopack; don't abstract signing.
- **[Trade-off] vpk CLI dependency** → Requires `vpk` tool installed. Document in prerequisites.

## Testing Strategy

- **CT**: `VelopackAutoUpdateProvider` with mock `UpdateManager` (or interface wrapper)
- **CT**: `PackageCommand` argument parsing and validation
- **IT**: Package a sample app and verify installer works (manual or CI)
- **IT**: Auto-update flow: publish v1, update to v2, verify restart
