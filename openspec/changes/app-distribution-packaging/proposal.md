## Why

Fulora has a complete developer journey from `dotnet new` to `dotnet build`, but the "last mile" — packaging into installable applications with code signing, notarization, and auto-update — is completely absent. Developers must manually figure out MSI/DMG/AppImage creation, code signing, and wiring `IAutoUpdatePlatformProvider`. This is the #1 barrier to shipping real products. Electron has electron-builder; Tauri has built-in packaging; Fulora needs an equivalent. Goal: E1 (Project Template), Phase 12 — Production Deployment Readiness.

## What Changes

- New CLI command `fulora package` that wraps `dotnet publish` + Velopack (`vpk pack`)
- Concrete `VelopackAutoUpdateProvider : IAutoUpdatePlatformProvider` for Windows, macOS, Linux
- Platform-specific packaging targets: MSI/Setup.exe (Windows), DMG/pkg (macOS), AppImage/deb (Linux)
- Code signing and macOS notarization integration in `fulora package`
- Release feed generation (`releases.{channel}.json`) compatible with `IAutoUpdateService`
- Nuke build target `PackageApp` for CI integration
- Documentation: "Shipping Your App" guide

## Capabilities

### New Capabilities

- `app-distribution-packaging`: CLI-driven app packaging, signing, and distribution
- `velopack-auto-update`: Concrete `IAutoUpdatePlatformProvider` implementation via Velopack

### Modified Capabilities

- `cli-commands`: Add `fulora package` command
- `auto-update-framework`: Wire Velopack provider into DI

## Non-goals

- App store submission (Microsoft Store, Mac App Store)
- Mobile distribution (APK/IPA — different pipeline)
- Custom installer UI (use platform-standard installers)
- Hosting an update server (use GitHub Releases, S3, or any static file host)

## Impact

- New: `src/Agibuild.Fulora.Runtime/VelopackAutoUpdateProvider.cs`
- New: `src/Agibuild.Fulora.Cli/Commands/PackageCommand.cs`
- New: `docs/shipping-your-app.md`
- Modified: `build/Build.cs` (add `PackageApp` target)
- New dependency: `Velopack` NuGet package
- New tests for `VelopackAutoUpdateProvider`
