## 1. Template: Add Web.csproj and restructure

- [x] 1.1 Create `templates/agibuild-hybrid/HybridApp.Web.Vite.React/HybridApp.Web.Vite.React.csproj` using `Microsoft.Build.NoTargets/3.7.134` SDK with `NpmInstall` and `WebBuild` targets (incremental via Inputs/Outputs + stamp files). Deliverable: web-msbuild-integration; Acceptance: file exists with correct SDK, targets, and `<None Include>` items for IDE visibility.
- [x] 1.2 Create `templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/HybridApp.Web.Vite.Vue.csproj` with identical MSBuild structure as the React variant. Deliverable: web-msbuild-integration; Acceptance: file exists and mirrors React Web.csproj structure.
- [x] 1.3 Update `templates/agibuild-hybrid/HybridApp.sln` to include the Web project with conditional `#if` preprocessing for `react` and `vue` framework choices, including solution configuration entries. Deliverable: web-msbuild-integration; Acceptance: `.sln` contains conditional Web project entries for both frameworks.
- [x] 1.4 Update `templates/agibuild-hybrid/.template.config/template.json` to add `sources[].rename` rules mapping `HybridApp.Web.Vite.React/` → `HybridApp.Web/` and `HybridApp.Web.Vite.Vue/` → `HybridApp.Web/` (conditional on framework choice). Deliverable: web-msbuild-integration; Acceptance: `dotnet new agibuild-hybrid -n Foo --framework react` produces `Foo.Web/Foo.Web.csproj`.
- [x] 1.5 Update `templates/agibuild-hybrid/HybridApp.Desktop/HybridApp.Desktop.csproj` to add `ProjectReference` to Web.csproj (`ReferenceOutputAssembly="false"`, `SkipGetTargetFrameworkProperties="true"`) conditional on non-vanilla framework, and add Release `EmbeddedResource` from Web `dist/`. Deliverable: web-msbuild-integration; Acceptance: Desktop.csproj references Web project and embeds dist output in Release.
- [x] 1.6 Update `templates/agibuild-hybrid/HybridApp.Bridge/HybridApp.Bridge.csproj` `BridgeTypeScriptOutputDir` conditions to use the new `HybridApp.Web` directory name. Deliverable: web-msbuild-integration; Acceptance: Bridge TS output resolves to `HybridApp.Web/src/bridge/generated`.

## 2. Sample: avalonia-react

- [x] 2.1 Create `samples/avalonia-react/AvaloniReact.Web/AvaloniReact.Web.csproj` using `Microsoft.Build.NoTargets/3.7.134` SDK with `NpmInstall` and `WebBuild` targets (self-contained, no shared imports). Deliverable: web-msbuild-integration; Acceptance: `dotnet build AvaloniReact.Web.csproj` runs `npm ci` and succeeds.
- [x] 2.2 Update `samples/avalonia-react/AvaloniReact.sln` to include the Web project with Debug/Release configurations. Deliverable: web-msbuild-integration; Acceptance: `.sln` lists 4 projects (Desktop, Bridge, Tests, Web).
- [x] 2.3 Update `samples/avalonia-react/AvaloniReact.Desktop/AvaloniReact.Desktop.csproj`: remove `BuildWebApp` target, add `ProjectReference` to Web.csproj, keep Release `EmbeddedResource` from Web dist. Deliverable: web-msbuild-integration; Acceptance: no `<Exec Command="npm` in Desktop.csproj; ProjectReference to Web exists.

## 3. Sample: avalonia-vue

- [x] 3.1 Create `samples/avalonia-vue/AvaloniVue.Web/AvaloniVue.Web.csproj` using `Microsoft.Build.NoTargets/3.7.134` SDK (self-contained). Deliverable: web-msbuild-integration; Acceptance: `dotnet build AvaloniVue.Web.csproj` runs `npm ci` and succeeds.
- [x] 3.2 Update `samples/avalonia-vue/AvaloniVue.sln` to include the Web project. Deliverable: web-msbuild-integration; Acceptance: `.sln` lists Web project.
- [x] 3.3 Update `samples/avalonia-vue/AvaloniVue.Desktop/AvaloniVue.Desktop.csproj`: remove `BuildWebApp` target, add `ProjectReference` to Web.csproj. Deliverable: web-msbuild-integration; Acceptance: no inline npm targets in Desktop.csproj.

## 4. Sample: avalonia-ai-chat

- [x] 4.1 Create `samples/avalonia-ai-chat/AvaloniAiChat.Web/AvaloniAiChat.Web.csproj` using `Microsoft.Build.NoTargets/3.7.134` SDK (self-contained). Deliverable: web-msbuild-integration; Acceptance: `dotnet build AvaloniAiChat.Web.csproj` runs `npm ci` and succeeds.
- [x] 4.2 Update `samples/avalonia-ai-chat/AvaloniAiChat.sln` to include the Web project. Deliverable: web-msbuild-integration; Acceptance: `.sln` lists Web project.
- [x] 4.3 Update `samples/avalonia-ai-chat/AvaloniAiChat.Desktop/AvaloniAiChat.Desktop.csproj`: remove `BuildWebApp` target, add `ProjectReference` to Web.csproj. Deliverable: web-msbuild-integration; Acceptance: no inline npm targets in Desktop.csproj.

## 5. Sample: showcase-todo

- [x] 5.1 Create `samples/showcase-todo/ShowcaseTodo.Web/ShowcaseTodo.Web.csproj` using `Microsoft.Build.NoTargets/3.7.134` SDK (self-contained). Deliverable: web-msbuild-integration; Acceptance: `dotnet build ShowcaseTodo.Web.csproj` runs `npm ci` and succeeds.
- [x] 5.2 Update `samples/showcase-todo/ShowcaseTodo.sln` to include the Web project. Deliverable: web-msbuild-integration; Acceptance: `.sln` lists Web project.
- [x] 5.3 Update `samples/showcase-todo/ShowcaseTodo.Desktop/ShowcaseTodo.Desktop.csproj`: remove `BuildWebApp` target, add `ProjectReference` to Web.csproj. Deliverable: web-msbuild-integration; Acceptance: no inline npm targets in Desktop.csproj.

## 6. Build System Alignment

- [x] 6.1 Review `build/Build.Samples.cs` `EnsureSampleWebDepsInstalledAsync` — verify it remains compatible (npm install may already have run via MSBuild). Add skip logic if `node_modules/.install-stamp` exists and is up-to-date. Deliverable: web-msbuild-integration; Acceptance: `nuke StartReactApp` still works correctly.
- [x] 6.2 Update sample `README.md` files to document the unified `dotnet build` workflow alongside existing `npm run dev` + `dotnet run` workflow. Deliverable: web-msbuild-integration; Acceptance: README reflects new build entry point.

## 7. Validation

- [x] 7.1 Run `dotnet build` on each updated sample `.sln` (react, vue, ai-chat, todo) and verify Web project builds successfully with npm install. Deliverable: web-msbuild-integration; Acceptance: all 4 solutions build without errors.
- [x] 7.2 Run `dotnet build -c Release` on `AvaloniReact.sln` and verify `dist/` is produced and Desktop embeds it. Deliverable: web-msbuild-integration; Acceptance: Release build produces embedded resources from dist.
- [x] 7.3 Run `nuke TemplateE2E` to verify template instantiation and build for all framework choices (react, vue, vanilla). Deliverable: web-msbuild-integration; Acceptance: all template E2E tests pass.
- [x] 7.4 Run `nuke StartReactApp` to verify Nuke dev-server orchestration still works. Deliverable: web-msbuild-integration; Acceptance: React sample launches with Vite HMR.
- [x] 7.5 Run `nuke Test` to verify no regressions. Deliverable: web-msbuild-integration; Acceptance: all existing tests pass.
