## Why

Fulora's hybrid app structure splits C# projects (.sln) and web frontend projects (npm/Vite) into separate development contexts. Developers must manage two toolchains independently — `dotnet build` for .NET and `npm install`/`npm run build` for the frontend — with no unified build entry point. This friction exists in both the `agibuild-hybrid` template output and all sample applications.

This directly impacts **E1 (Project Template)** and **G2 (SPA Hosting)** developer experience: a `dotnet new agibuild-hybrid` project should produce a solution where `dotnet build` handles the full stack, and IDE solution explorers show both C# and web source files. Current state requires manual terminal coordination and hides web source from solution-aware tools (VS, Rider).

## What Changes

- **Add a `Microsoft.Build.NoTargets` SDK project** (e.g., `MyApp.Web.csproj`) inside each web frontend directory, providing MSBuild-integrated npm lifecycle (install, build) with incremental build support.
- **Include the Web project in `.sln` files** for templates and all samples, enabling unified `dotnet build` and IDE visibility.
- **Remove inline `BuildWebApp` MSBuild targets** from Desktop `.csproj` files — web build responsibility moves to the dedicated Web project.
- **Add `ProjectReference`** from Desktop to Web (`ReferenceOutputAssembly="false"`) to guarantee correct build order.
- **Rename template web directories** from `HybridApp.Web.Vite.React` / `HybridApp.Web.Vite.Vue` to `HybridApp.Web` via template.json rename rules, so instantiated projects use the clean `MyApp.Web` naming.
- **Update `template.json`** with directory rename, `.sln` conditional inclusion, and framework-based exclusion for the Web `.csproj`.

## Non-goals

- **Replacing Nuke dev-server orchestration**: `npm run dev` (Vite HMR server) process lifecycle management stays in `Build.Samples.cs`. MSBuild is for build, not for long-running dev servers.
- **Shared MSBuild .targets file**: Each project (template output, sample) is self-contained. No cross-project import of shared build logic.
- **Using `.esproj`**: `Microsoft.VisualStudio.JavaScript.SDK` has known cross-platform defects (macOS path bugs, Linux build failures). Not suitable for a cross-platform framework.

## Capabilities

### New Capabilities

- `web-msbuild-integration`: MSBuild-based npm lifecycle integration for web frontend projects using `Microsoft.Build.NoTargets` SDK, enabling unified `dotnet build` across C# and web layers.

### Modified Capabilities

_(none — no existing spec-level requirements change)_

## Impact

- **Templates**: `agibuild-hybrid` template gains a Web `.csproj`, updated `.sln`, updated Desktop `.csproj`, updated `template.json`, and directory rename rules.
- **Samples**: `avalonia-react`, `avalonia-vue`, `avalonia-ai-chat`, `showcase-todo` each gain a Web `.csproj`, updated `.sln`, updated Desktop `.csproj`.
- **Build system**: `Build.Samples.cs` can optionally simplify `EnsureSampleWebDepsInstalledAsync` (npm install now runs via `dotnet build`), but Vite dev server orchestration remains.
- **Dependencies**: Adds `Microsoft.Build.NoTargets` NuGet SDK package (MIT license, Microsoft-maintained).
- **CI**: No breaking change — `dotnet build` on solution now includes Web project, which is additive.
