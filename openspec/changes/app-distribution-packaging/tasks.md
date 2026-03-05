# App Distribution & Packaging — Tasks

## 1. VelopackAutoUpdateProvider

- [x] 1.1 Add `Velopack` NuGet package dependency to Runtime project
- [x] 1.2 Create `VelopackAutoUpdateProvider : IAutoUpdatePlatformProvider`
- [x] 1.3 Implement `CheckForUpdateAsync` using HTTP feed parsing (Velopack-compatible)
- [x] 1.4 Implement `DownloadUpdateAsync` with progress reporting
- [x] 1.5 Implement `VerifyPackageAsync` (SHA256 verification)
- [x] 1.6 Implement `ApplyUpdateAsync` (stage + restart)
- [x] 1.7 Implement `GetCurrentVersion` from assembly metadata
- [x] 1.8 Injectable HttpClient constructor for testability
- [x] 1.9 CT: CheckForUpdate with mock feed returning newer version
- [x] 1.10 CT: CheckForUpdate with mock feed returning no update
- [x] 1.11 CT: DownloadUpdate reports progress correctly

## 2. fulora package CLI Command

- [x] 2.1 Create `PackageCommand` in `src/Agibuild.Fulora.Cli/Commands/`
- [x] 2.2 Define CLI options: `--project`, `--runtime`, `--version`, `--output`, `--icon`, `--sign-params`, `--notarize`, `--channel`
- [x] 2.3 Implement `dotnet publish` invocation with correct arguments
- [x] 2.4 Implement `vpk pack` invocation with mapped arguments
- [x] 2.5 Validate prerequisites: `dotnet` and `vpk` in PATH
- [x] 2.6 Error handling: missing project, invalid runtime, vpk not found
- [x] 2.7 CT: argument parsing and validation tests
- [x] 2.8 CT: command generates correct options structure

## 3. Nuke Build Integration

- [ ] 3.1 Add `PackageApp` target to `build/Build.cs`
- [ ] 3.2 Target invokes `dotnet publish` + `vpk pack` for configured sample app
- [ ] 3.3 Add packaging parameters to Nuke parameter model

## 4. Documentation

- [x] 4.1 Create `docs/shipping-your-app.md`
- [x] 4.2 Document: prerequisites (vpk install), basic packaging, code signing, notarization
- [x] 4.3 Document: auto-update setup with VelopackAutoUpdateProvider
- [x] 4.4 Document: CI integration (GitHub Actions workflow example)
- [x] 4.5 Add sample update feed JSON structure
