# Fulora Platform Phase 2-5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the next product-platform phases after the docs-first migration by adding capability security governance, a unified diagnostics model, and layer-aware DX defaults without regressing existing runtime or release gates.

**Architecture:** Build on existing Fulora primitives instead of introducing a second policy or diagnostics stack. Reuse `IBridgePlugin` manifests, shell host-capability execution, bridge tracers, and CLI/template commands, then converge them into one governed capability + diagnostics model with minimal surface changes per task.

**Tech Stack:** .NET 10, Nuke build, xUnit, DocFX, System.CommandLine, existing Fulora runtime/bridge/plugin/template infrastructure.

---

### Task 1: Add Capability Metadata to Official Plugins

**Files:**
- Modify: `src/Agibuild.Fulora.Core/IBridgePlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.FileSystem/FileSystemPlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.HttpClient/HttpClientPlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.Notifications/NotificationPlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.AuthToken/AuthTokenPlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.Biometric/BiometricPlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.LocalStorage/LocalStoragePlugin.cs`
- Modify: `plugins/Agibuild.Fulora.Plugin.Database/DatabasePlugin.cs`
- Modify: `docs/plugin-authoring-guide.md`
- Test: `tests/Agibuild.Fulora.UnitTests/*Plugin*Tests.cs`

- [ ] **Step 1: Add failing tests for plugin capability metadata**

Write focused unit tests that require official plugins to expose stable metadata:
- plugin id
- required capabilities
- optional capabilities
- security notes
- platform constraints

- [ ] **Step 2: Run focused tests to verify they fail**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Plugin|BridgePlugin"`
Expected: FAIL because plugin manifests currently expose services only.

- [ ] **Step 3: Extend plugin manifest contracts**

Add a minimal metadata shape to `IBridgePlugin`/plugin descriptors without breaking existing registration semantics.

- [ ] **Step 4: Populate metadata for official plugins**

Seed each official plugin with capability metadata aligned to the docs-first registry:
- filesystem read/write/pick
- http outbound
- notification post
- auth token read/write
- biometric prompt
- local storage read/write
- database read/write

- [ ] **Step 5: Update plugin authoring guide**

Document the new metadata fields and required capability declaration pattern.

- [ ] **Step 6: Re-run focused tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Plugin|BridgePlugin"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/Agibuild.Fulora.Core/IBridgePlugin.cs plugins docs/plugin-authoring-guide.md tests/Agibuild.Fulora.UnitTests
git commit -m "feat: add plugin capability metadata"
```

### Task 2: Add Runtime Capability Policy Evaluator and Deny Diagnostics

**Files:**
- Modify: `src/Agibuild.Fulora.Runtime/Shell/WebViewHostCapabilityBridge.cs`
- Modify: `src/Agibuild.Fulora.Runtime/Shell/WebViewHostCapabilityExecutor.cs`
- Modify: `src/Agibuild.Fulora.Runtime/Shell/WebViewShellExperience.cs`
- Create: `src/Agibuild.Fulora.Runtime/Shell/WebViewCapabilityPolicyEvaluator.cs`
- Create: `src/Agibuild.Fulora.Runtime/Shell/WebViewCapabilityDescriptor.cs`
- Create: `src/Agibuild.Fulora.Runtime/Shell/WebViewCapabilityPolicyDecision.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/HostCapabilityBridgeTests.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/ShellExperienceTests.cs`

- [ ] **Step 1: Write failing tests for capability-id based authorization**

Require host-capability operations to surface a stable capability id and deny reason in test assertions.

- [ ] **Step 2: Run focused host-capability tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "HostCapabilityBridgeTests|ShellExperienceTests"`
Expected: FAIL because current authorization only returns operation/deny outcomes.

- [ ] **Step 3: Introduce capability descriptors and policy decisions**

Map current host capability operations onto stable ids such as:
- `clipboard.read`
- `clipboard.write`
- `filesystem.pick`
- `notification.post`
- `shell.external_open`
- `window.chrome.modify`

- [ ] **Step 4: Emit deny diagnostics with stable fields**

Ensure denied outcomes can be observed with:
- capability id
- source component
- deny reason
- effective policy decision
- correlation id or operation id when available

- [ ] **Step 5: Re-run focused host-capability tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "HostCapabilityBridgeTests|ShellExperienceTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Runtime/Shell tests/Agibuild.Fulora.UnitTests
git commit -m "feat: add runtime capability policy evaluator"
```

### Task 3: Expose Capability Visibility Through the CLI

**Files:**
- Modify: `src/Agibuild.Fulora.Cli/Program.cs`
- Modify: `src/Agibuild.Fulora.Cli/Commands/ListPluginsCommand.cs`
- Create: `src/Agibuild.Fulora.Cli/Commands/ListCapabilitiesCommand.cs`
- Create: `src/Agibuild.Fulora.Cli/Commands/InspectPluginCommand.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/Cli*Tests.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Write failing CLI tests**

Require support for:
- `fulora list capabilities`
- `fulora inspect plugin <package>`
- plugin capability summaries in `list plugins --check`

- [ ] **Step 2: Run focused CLI tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Cli|DocumentationGovernanceTests"`
Expected: FAIL because capability listing/inspection commands do not exist.

- [ ] **Step 3: Implement CLI capability discovery**

Read plugin package/project metadata and surface declared capabilities in a human-readable but deterministic format.

- [ ] **Step 4: Re-run focused CLI tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Cli|DocumentationGovernanceTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Cli tests/Agibuild.Fulora.UnitTests
git commit -m "feat: expose plugin capabilities in cli"
```

### Task 4: Unify Diagnostics Events and Sinks

**Files:**
- Create: `src/Agibuild.Fulora.Runtime/Diagnostics/FuloraDiagnosticsEvent.cs`
- Create: `src/Agibuild.Fulora.Runtime/Diagnostics/IFuloraDiagnosticsSink.cs`
- Create: `src/Agibuild.Fulora.Runtime/Diagnostics/MemoryFuloraDiagnosticsSink.cs`
- Create: `src/Agibuild.Fulora.Runtime/Diagnostics/LoggingFuloraDiagnosticsSink.cs`
- Modify: `src/Agibuild.Fulora.Runtime/BridgeTelemetryTracer.cs`
- Modify: `src/Agibuild.Fulora.Runtime/LoggingBridgeTracer.cs`
- Modify: `src/Agibuild.Fulora.Runtime/BridgeCallProfiler.cs`
- Modify: `src/Agibuild.Fulora.Runtime/WebViewCore.cs`
- Modify: `src/Agibuild.Fulora.Runtime/WebMessageBridgeOptions.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/TelemetryTests.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/BridgeDevToolsTests.cs`

- [ ] **Step 1: Write failing diagnostics-schema tests**

Require bridge/runtime diagnostics producers to emit one shared event envelope with:
- event name
- layer
- component
- duration/status fields when applicable

- [ ] **Step 2: Run focused diagnostics tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "TelemetryTests|BridgeDevToolsTests|BridgeErrorDiagnosticTests"`
Expected: FAIL because current tracers are bridge-specific and not unified.

- [ ] **Step 3: Introduce unified diagnostics event + sink abstraction**

Keep bridge tracers working, but adapt them to publish normalized diagnostics events through a shared sink.

- [ ] **Step 4: Re-run focused diagnostics tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "TelemetryTests|BridgeDevToolsTests|BridgeErrorDiagnosticTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Runtime/Diagnostics src/Agibuild.Fulora.Runtime tests/Agibuild.Fulora.UnitTests
git commit -m "feat: add unified diagnostics event model"
```

### Task 5: Make Templates and Scaffolding Layer-Aware

**Files:**
- Modify: `src/Agibuild.Fulora.Cli/Commands/NewCommand.cs`
- Modify: `src/Agibuild.Fulora.Cli/Commands/AddCommand.cs`
- Modify: `templates/agibuild-hybrid/.template.config/template.json`
- Modify: `templates/agibuild-hybrid/HybridApp.Desktop/**`
- Modify: `templates/agibuild-hybrid/HybridApp.Web.Vite.React/**`
- Modify: `templates/agibuild-hybrid/HybridApp.Web.Vite.Vue/**`
- Create: template placeholders such as `capabilities.json`, `compatibility-notes.md`, `release-checklist.md` where appropriate
- Test: `tests/Agibuild.Fulora.UnitTests/Cli*Tests.cs`
- Test: `build/Build.Templates.cs`

- [ ] **Step 1: Write failing scaffold tests**

Require new projects to generate:
- layer-aware structure
- capability policy placeholder
- compatibility/release placeholders
- explicit `--layer` selection for `add service`

- [ ] **Step 2: Run focused scaffold tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Cli|Template"`  
Run: `./build.sh --target PackTemplate`
Expected: FAIL until new scaffold defaults are implemented.

- [ ] **Step 3: Implement minimal layer-aware defaults**

Keep the first slice intentionally small:
- generate layer-aware folders/files
- add deny-by-default capability policy placeholder
- require `--layer` for service scaffolding

- [ ] **Step 4: Re-run focused scaffold tests**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Cli|Template"`  
Run: `./build.sh --target PackTemplate`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Agibuild.Fulora.Cli templates tests/Agibuild.Fulora.UnitTests build/Build.Templates.cs
git commit -m "feat: add layer-aware scaffolding defaults"
```

### Task 6: Full Verification for Phase 2-5 Slice

**Files:**
- Modify: any touched files required to fix final verification failures

- [ ] **Step 1: Run governance and runtime unit subset**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "DocumentationGovernanceTests|AutomationLaneGovernanceTests|HostCapabilityBridgeTests|ShellExperienceTests|TelemetryTests|BridgeDevToolsTests"`
Expected: PASS

- [ ] **Step 2: Run CLI/template verification subset**

Run: `dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "Cli|Template|Plugin"`
Expected: PASS

- [ ] **Step 3: Run docs and build checks**

Run: `dotnet docfx docs/docfx.json`  
Run: `./build.sh --target LayeringGovernance`  
Run: `./build.sh --target ReleaseCloseoutSnapshot`
Expected: PASS

- [ ] **Step 4: Run final legacy-remnant scan**

Run: `rg -n "openspec|OpenSpecStrictGovernance|opsx-" . --glob '!docs/superpowers/**' --glob '!.git/**'`
Expected: no matches

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: advance platform capability security and diagnostics"
```

## Notes for the Implementer

- Reuse the docs-first governance model already landed on this branch; do not reintroduce phase-based status sources.
- Keep plugin capability metadata and runtime capability policy aligned to `docs/framework-capabilities.json`.
- Prefer additive, minimally disruptive APIs for the first capability-security slice.
- Any new diagnostics abstraction must preserve current bridge tracer behavior while adding a normalized event path.
- Template and CLI changes should encode policy defaults, not just document them.
