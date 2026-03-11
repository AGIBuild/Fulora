## Context

Fulora hybrid apps consist of a .NET desktop host (Avalonia), a shared Bridge library (C#), and a web frontend (npm/Vite). Currently the web frontend lives outside the .sln — it has no `.csproj`, is invisible to solution-aware IDEs, and its build lifecycle (`npm install`, `npm run build`) is orchestrated in two separate places:

1. **Desktop `.csproj`**: inline `BuildWebApp` MSBuild target (Release only)
2. **Nuke `Build.Samples.cs`**: `EnsureSampleWebDepsInstalledAsync` + `StartDesktopAppAsync`

This duplication exists across 4 samples (`avalonia-react`, `avalonia-vue`, `avalonia-ai-chat`, `showcase-todo`) and is absent from the `agibuild-hybrid` template (template Desktop.csproj has no `BuildWebApp` target at all — it only embeds static `wwwroot/` files).

The `agibuild-hybrid` template currently names web directories `HybridApp.Web.Vite.React` / `HybridApp.Web.Vite.Vue`. After sourceName replacement, users get `MyApp.Web.Vite.React` — unnecessarily verbose. The directory should be `MyApp.Web`.

## Goals / Non-Goals

**Goals:**

- Single `dotnet build` builds the entire solution (C# + web frontend)
- Web frontend files visible in VS/Rider Solution Explorer
- Each project (template output, sample) is fully self-contained
- Template produces the correct structure out of the box (`dotnet new agibuild-hybrid`)
- Incremental npm builds (skip when source unchanged)
- Web build concern isolated from Desktop project

**Non-Goals:**

- Replacing Nuke's Vite dev server process management (`npm run dev` lifecycle)
- Sharing MSBuild targets across projects (each Web.csproj is self-contained)
- Using `.esproj` (cross-platform defects: macOS path bugs, Linux build failures)
- Changing the `@agibuild/bridge` npm package or Bridge source generator behavior

## Decisions

### Decision 1: Use `Microsoft.Build.NoTargets` SDK for the Web project

**Choice**: `Microsoft.Build.NoTargets/3.7.134` SDK

**Alternatives considered**:

| Option | Verdict |
|--------|---------|
| `Microsoft.VisualStudio.JavaScript.SDK` (.esproj) | Rejected — cross-platform bugs on macOS/Linux, not open-source |
| `Microsoft.NET.Sdk` with `EnableDefaultCompileItems=false` | Rejected — semantic misuse, SDK is for compiling C# |
| No `.csproj`, keep npm separate | Rejected — doesn't solve IDE visibility or unified build |

**Rationale**: NoTargets is the purpose-built MSBuild SDK for projects that orchestrate builds without producing assemblies. It's cross-platform, open-source (MIT), and Microsoft-maintained. It requires `TargetFramework` (known limitation) but has no compilation overhead.

### Decision 2: Web.csproj lives in the frontend source directory

The `.csproj` is co-located with `package.json`, `vite.config.ts`, and `src/` — not in a separate wrapper directory.

**Rationale**: The project manages the lifecycle of this directory. Co-location avoids path indirection and makes the project self-explanatory.

### Decision 3: Desktop references Web via `ProjectReference` (build-order only)

```xml
<ProjectReference Include="..\MyApp.Web\MyApp.Web.csproj"
                  ReferenceOutputAssembly="false"
                  SkipGetTargetFrameworkProperties="true" />
```

**Rationale**: NoTargets produces no output assembly. The reference purely ensures MSBuild builds Web before Desktop. `SkipGetTargetFrameworkProperties` avoids TFM mismatch warnings.

### Decision 4: Incremental builds via MSBuild `Inputs/Outputs` + stamp files

- `NpmInstall` target: Input=`package.json`, Output=`node_modules/.install-stamp`
- `WebBuild` target: Input=`src/**;package.json;vite.config.ts`, Output=`dist/.build-stamp`

**Rationale**: Without incremental markers, every `dotnet build` would re-run `npm ci` (~5-15s) and `npm run build` (~3-10s). Stamp files are the standard MSBuild pattern for external tool integration.

### Decision 5: Template directory rename `HybridApp.Web.Vite.{React,Vue}` → `HybridApp.Web`

Use `template.json` `sources[].rename` rules to map framework-specific directories to the clean `HybridApp.Web` name. After `sourceName` replacement, user gets `MyApp.Web`.

**Rationale**: The build tool (Vite) and framework (React/Vue) are `package.json` concerns, not directory-name concerns. Users don't need this metadata in their project structure.

### Decision 6: Remove inline `BuildWebApp` target from Desktop.csproj

The `BuildWebApp` MSBuild target currently in sample Desktop `.csproj` files is deleted. Web build is now Web.csproj's responsibility. Desktop only declares the `ProjectReference` and the `EmbeddedResource` for Release.

**Rationale**: Separation of concerns — each project owns its own build lifecycle.

### Decision 7: `npm ci` vs `npm install`

Use `npm ci` for deterministic installs (honors `package-lock.json`).

**Rationale**: `npm ci` is the recommended command for reproducible builds. It's faster in CI (deletes `node_modules` first) and guaranteed consistent.

## Testing Strategy

- **Template E2E (`nuke TemplateE2E`)**: Already validates that `dotnet new agibuild-hybrid --framework react` produces a buildable solution. After this change, `dotnet build` on the generated solution will also build the Web project — existing test coverage applies.
- **Sample build validation**: `dotnet build AvaloniReact.sln` in CI should succeed with the new Web project included. No new test files needed — the build itself is the test.
- **Manual validation**: `dotnet build -c Release` should produce `dist/` output and Desktop should embed it as resources (existing behavior, new orchestration path).

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| `Microsoft.Build.NoTargets` requires `TargetFramework` declaration despite not compiling | Use `net10.0` to match host project; this is a cosmetic annoyance, not a functional issue |
| First `dotnet build` after clone is slower (runs `npm ci`) | Incremental stamp file ensures subsequent builds skip npm; document in README |
| Nuke `Build.Samples.cs` partially overlaps with Web.csproj npm targets | Keep Nuke for dev-server orchestration only; gradually simplify `EnsureSampleWebDepsInstalledAsync` to be a no-op when `dotnet build` has already run |
| Template `.sln` conditional preprocessing (`#if`) may be fragile | Validate via existing `nuke TemplateE2E` which exercises all framework choices |
| `npm ci` deletes `node_modules` on every invocation if stamp is stale | Stamp file keyed on `package.json` change minimizes this; acceptable trade-off for determinism |
