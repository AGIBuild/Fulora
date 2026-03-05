## Purpose

Define requirements for the `fulora package` CLI command, `VelopackAutoUpdateProvider`, and the end-to-end app distribution workflow.

## ADDED Requirements

### Requirement: fulora package produces platform installers

`fulora package` SHALL build and package a Fulora app into platform-specific installers.

#### Scenario: Package for Windows
- **GIVEN** a Fulora app project targeting `net10.0`
- **WHEN** `fulora package --project MyApp.csproj --runtime win-x64 --version 1.0.0` is executed
- **THEN** the command SHALL run `dotnet publish` with self-contained and single-file options
- **AND** SHALL invoke `vpk pack` to produce a Setup.exe installer
- **AND** SHALL generate `releases.win-x64.json` feed file
- **AND** the output directory SHALL contain the installer and feed

#### Scenario: Package for macOS
- **GIVEN** a Fulora app project
- **WHEN** `fulora package --project MyApp.csproj --runtime osx-arm64 --version 1.0.0` is executed
- **THEN** the command SHALL produce a `.app` bundle and DMG installer
- **AND** SHALL generate `releases.osx-arm64.json` feed file

#### Scenario: Package for Linux
- **GIVEN** a Fulora app project
- **WHEN** `fulora package --project MyApp.csproj --runtime linux-x64 --version 1.0.0` is executed
- **THEN** the command SHALL produce an AppImage
- **AND** SHALL generate `releases.linux-x64.json` feed file

#### Scenario: Missing Velopack CLI
- **GIVEN** `vpk` is not installed or not in PATH
- **WHEN** `fulora package` is executed
- **THEN** the command SHALL print an error with installation instructions
- **AND** SHALL exit with non-zero code

### Requirement: Code signing and notarization

Windows and macOS installers SHALL support code signing and notarization via Velopack integration.

#### Scenario: Windows code signing
- **GIVEN** `--sign-params` is provided with signing certificate info
- **WHEN** `fulora package` is executed for Windows
- **THEN** the command SHALL pass signing parameters to Velopack
- **AND** the resulting installer and exe SHALL be signed

#### Scenario: macOS notarization
- **GIVEN** `--notarize` flag is provided with Apple credentials
- **WHEN** `fulora package` is executed for macOS
- **THEN** the command SHALL notarize the app bundle via Apple's notarization service
- **AND** SHALL staple the notarization ticket to the DMG

### Requirement: VelopackAutoUpdateProvider implements IAutoUpdatePlatformProvider

`VelopackAutoUpdateProvider` SHALL implement `IAutoUpdatePlatformProvider` for Velopack-based update delivery.

#### Scenario: Check for update
- **GIVEN** a `VelopackAutoUpdateProvider` configured with `FeedUrl`
- **WHEN** `CheckForUpdateAsync()` is called
- **THEN** it SHALL download and parse the release feed JSON
- **AND** SHALL return `UpdateInfo` if a newer version is available
- **AND** SHALL return null if no update is available

#### Scenario: Download and apply update
- **GIVEN** an update is available
- **WHEN** `DownloadUpdateAsync(updateInfo)` is called
- **THEN** it SHALL download the update package with progress reporting
- **AND** `VerifyPackageAsync()` SHALL verify the package integrity (SHA256)
- **AND** `ApplyUpdateAsync()` SHALL stage the update and request restart

#### Scenario: DI registration via UseVelopack
- **GIVEN** `services.AddFulora(b => b.AddAutoUpdate(o => o.UseVelopack()))` is configured
- **WHEN** the app resolves `IAutoUpdateService`
- **THEN** it SHALL be backed by `VelopackAutoUpdateProvider`
- **AND** `AutoUpdateOptions.FeedUrl` SHALL be used for update checks
