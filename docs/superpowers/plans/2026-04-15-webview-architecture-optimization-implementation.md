# WebView Architecture Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the remaining WebView architecture cleanup by making platform selection provider-first and plugin-style, keeping `WebView.cs` as a true Avalonia shell, and only performing additional runtime cleanup when a verified design smell still exists after each slice.

**Architecture:** Land this work in narrow, reversible slices. First extract ordered adapter candidate resolution out of `WebViewAdapterRegistry` and push legacy platform mapping behind an explicit compatibility helper so provider discovery, ordering, creation, and fallback are no longer mixed together. Then reassess the control-side runtimes: only remove proven ownership leaks or constructor noise, and keep Avalonia visual-tree, overlay geometry, and window behavior inside the Avalonia shell.

**Tech Stack:** .NET 10, C#, Avalonia, existing WebView adapter/runtime abstractions, xUnit, existing governance/unit/integration tests, git.

---

## File Map

### Platform selection chain

- `src/Agibuild.Fulora.Adapters.Abstractions/IWebViewPlatformProvider.cs`
  - Keep the provider SPI small: identity, priority, platform capability, adapter construction.
- `src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterRegistry.cs`
  - Keep registration storage and thin orchestration only; do not let it continue to own all resolution logic.
- `src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterCandidateResolver.cs`
  - New internal helper that owns candidate projection, ordering, deterministic tie-break rules, instance creation, and failure text.
- `src/Agibuild.Fulora.Adapters.Abstractions/WebViewLegacyAdapterCompatibility.cs`
  - New internal helper that isolates legacy platform mapping and legacy-registration projection so provider resolution no longer depends on a central OS switch.
- `src/Agibuild.Fulora.Runtime/WebViewAdapterFactory.cs`
  - Continue to own assembly loading/discovery only; delegate resolution cleanly.
- `src/Agibuild.Fulora.Adapters.Windows/WindowsAdapterModule.cs`
- `src/Agibuild.Fulora.Adapters.MacOS/MacOSAdapterModule.cs`
- `src/Agibuild.Fulora.Adapters.iOS/iOSAdapterModule.cs`
- `src/Agibuild.Fulora.Adapters.Android/AndroidAdapterModule.cs`
- `src/Agibuild.Fulora.Adapters.Gtk/GtkAdapterModule.cs`
  - Keep provider registration only; do not move selection policy back into module initializers.

### Control-side runtimes

- `src/Agibuild.Fulora.Avalonia/WebView.cs`
  - Stay as the Avalonia shell: property registration, visual lifecycle, overlay geometry, minimal composition.
- `src/Agibuild.Fulora.Avalonia/WebViewControlRuntime.cs`
  - Keep core/state ownership and forward-only API surface.
- `src/Agibuild.Fulora.Avalonia/WebViewControlLifecycleRuntime.cs`
  - Keep attach/destroy behavior only.
- `src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntime.cs`
  - Keep core event wiring only.
- `src/Agibuild.Fulora.Avalonia/WebViewControlInteractionRuntime.cs`
  - Keep interaction handler aggregation only.
- `src/Agibuild.Fulora.Avalonia/WebViewControlStateRuntime.cs`
  - Keep Avalonia property/state coordination only.
- `src/Agibuild.Fulora.Avalonia/WebViewHostClosingRuntime.cs`
  - Keep host-window closing coordination only.
- `src/Agibuild.Fulora.Avalonia/WebViewOverlayRuntime.cs`
  - Keep overlay host lifecycle only; do not absorb layout/geometry math from `WebView.cs`.
- `src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntimeBindings.cs`
  - Optional new internal file for grouped constructor dependencies if event-runtime callback noise remains after platform work.

### Tests

- `tests/Agibuild.Fulora.UnitTests/WebViewAdapterRegistryTests.cs`
  - Provider/legacy coexistence, resolution ordering, failure handling.
- `tests/Agibuild.Fulora.UnitTests/WebViewAdapterCandidateResolverTests.cs`
  - New focused tests for resolver behavior without registry storage noise.
- `tests/Agibuild.Fulora.UnitTests/WebViewLegacyAdapterCompatibilityTests.cs`
  - New focused tests for legacy compatibility mapping/projection.
- `tests/Agibuild.Fulora.UnitTests/BranchCoverageRound3Tests.cs`
  - Remove duplicated current-platform mapping helpers once compatibility helper exists.
- `tests/Agibuild.Fulora.UnitTests/WebViewControlRuntimeTests.cs`
- `tests/Agibuild.Fulora.UnitTests/WebViewControlLifecycleRuntimeTests.cs`
- `tests/Agibuild.Fulora.UnitTests/WebViewControlEventRuntimeTests.cs`
- `tests/Agibuild.Fulora.UnitTests/WebViewHostClosingRuntimeTests.cs`
- `tests/Agibuild.Fulora.UnitTests/WebViewOverlayRuntimeTests.cs`
  - Preserve behavior while refactoring control-side boundaries.
- `tests/Agibuild.Fulora.Governance.UnitTests/WebViewCoreHotspotGovernanceTests.cs`
  - Keep hotspot evidence intact if file/method names change.

---

## Scope Guardrails

- Do not reintroduce shell-side platform branching.
- Do not add a new centralized OS switch for the provider path. If legacy registrations still need OS mapping, keep it in a compatibility-only helper.
- Do not move `VisualRoot`, `TranslatePoint`, `TopLevel`, `RenderScaling`, overlay geometry, or Avalonia window behavior into shared runtime/core layers.
- Do not create a new god runtime.
- Do not merge multiple runtimes back into a generic helper just to reduce file count.
- Do not add dependencies.
- Do not skip TDD, focused verification, full regression, or commit checkpoints.
- Stop after any slice if the next refactor would be speculative instead of evidence-driven.

## Architectural Invariants

- `WebViewAdapterCandidateResolver` must resolve **provider candidates without consulting** `WebViewLegacyAdapterCompatibility` or any equivalent shared OS-switch helper.
- `WebViewLegacyAdapterCompatibility` exists only to:
  - map the current runtime OS into the legacy enum path
  - project legacy registrations for the current platform
  - expose a shared test seam so unit tests stop cloning current-platform mapping logic
- Ordered adapter resolution must be explicit and deterministic:
  - priority descending
  - provider candidates beat legacy candidates when priority is equal
  - within the same source and priority, use a stable ordinal key (`provider.Id` or `registration.AdapterId`)
- `WebViewControlRuntime` must remain limited to:
  - core identity/state ownership
  - attach/unavailable authority
  - pending bridge-tracer handoff
  - forward-only access to core APIs
- `WebViewControlRuntime` must not absorb:
  - native attach/detach sequencing
  - core event subscription orchestration
  - host-window closing policy
  - overlay visual hooks or geometry
  - Avalonia property synchronization

## Control Runtime Acceptance Check

After every landed task, verify that:

- `WebViewControlRuntime` gained no new collaborator references beyond `WebViewCore`-facing state ownership.
- `WebViewControlLifecycleRuntime` still owns attach/destroy sequencing.
- `WebViewControlEventRuntime` still owns core event wiring.
- `WebView.cs` still owns Avalonia-only composition and intentional overlay geometry math.

## Execution Order

This plan intentionally has **required** and **conditional** tasks.

- **Required first:** Task 1 and Task 2.
- **Conditional follow-up:** Task 3 and Task 4 only execute if Task 2 still leaves a verified design smell.
- Re-review the architecture after every task. If the ownership is clear and the next task would be cleanup-for-cleanup’s-sake, stop.

## Conditional Task Triggers

- **Task 3 trigger:** `WebViewControlLifecycleRuntimeTests` can be made to fail by passing a throwing `setAdapterUnavailable` callback, proving lifecycle still depends on shell-only state that production wiring ignores.
- **Task 4 trigger:** evaluate this after Task 3, or directly after Task 2 if Task 3 is skipped. Task 4 is allowed only when both of these are still true:
  - `WebViewControlEventRuntime` still requires more than 10 positional constructor arguments.
  - `WebViewControlEventRuntimeTests.cs` and `WebViewControlLifecycleRuntimeTests.cs` still each contain at least one setup block that must pass the full positional constructor argument list instead of a smaller named grouping.

## Mandatory Regression Bundle

Run this after each landed task, after the focused tests are already green:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj
dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj
dotnet test Agibuild.Fulora.sln --no-restore -m:1
```

Expected: PASS with no newly introduced failures.

---

### Task 1: Separate ordered adapter candidate resolution from registry storage

**Files:**
- Create: `src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterCandidateResolver.cs`
- Modify: `src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterRegistry.cs`
- Create: `tests/Agibuild.Fulora.UnitTests/WebViewAdapterCandidateResolverTests.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/WebViewAdapterRegistryTests.cs`

- [ ] **Step 1: Write the failing resolver-focused tests**

Create `tests/Agibuild.Fulora.UnitTests/WebViewAdapterCandidateResolverTests.cs` with tests that require:

```csharp
[Fact]
public void Resolve_prefers_highest_priority_candidate_across_provider_and_legacy_sources()

[Fact]
public void Resolve_uses_a_deterministic_secondary_order_when_priorities_match()

[Fact]
public void Resolve_returns_a_failure_reason_when_no_candidates_exist()
```

Also extend `WebViewAdapterRegistryTests.cs` so the registry tests verify only storage/registration behavior plus delegation to the resolver, instead of re-testing the entire ordering pipeline.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewAdapterCandidateResolverTests|FullyQualifiedName~WebViewAdapterRegistryTests"
```

Expected: FAIL because `WebViewAdapterCandidateResolver` does not exist yet and the registry still owns the full resolution path.

- [ ] **Step 3: Implement the minimal resolver extraction**

Add `WebViewAdapterCandidateResolver` as an internal helper that:

- accepts provider candidates and legacy candidates as inputs
- projects them into a single ordered candidate set
- applies a deterministic tie-break rule after priority
- creates the winning adapter instance
- returns a stable failure reason when no candidate is available

Update `WebViewAdapterRegistry` so it keeps:

- provider registration storage
- legacy registration storage
- thin delegation to the resolver

Do not change assembly loading in this task. Do not remove the legacy path yet. The intent is to split **catalog** from **resolution**, not to redesign the whole chain in one diff.

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewAdapterCandidateResolverTests|FullyQualifiedName~WebViewAdapterRegistryTests"
```

Expected: PASS

- [ ] **Step 5: Run the mandatory regression bundle**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj
dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj
dotnet test Agibuild.Fulora.sln --no-restore -m:1
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterCandidateResolver.cs src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterRegistry.cs tests/Agibuild.Fulora.UnitTests/WebViewAdapterCandidateResolverTests.cs tests/Agibuild.Fulora.UnitTests/WebViewAdapterRegistryTests.cs
git commit -m "$(cat <<'EOF'
Clarify WebView adapter candidate resolution ownership

Constraint: keep provider-first discovery and preserve existing adapter behavior while splitting catalog from resolution.
Rejected: registry-owned full resolution pipeline | broad platform-selection redesign in one diff
Confidence: medium
Scope-risk: narrow
Reversibility: clean
Directive: extract ordered adapter candidate resolution into a focused helper and leave registration storage in the registry.
Tested: dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter \"FullyQualifiedName~WebViewAdapterCandidateResolverTests|FullyQualifiedName~WebViewAdapterRegistryTests\"; dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj; dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj; dotnet test Agibuild.Fulora.sln --no-restore -m:1
Not-tested: manual platform smoke on Windows/macOS/iOS/Android/Gtk

EOF
)"
```

---

### Task 2: Push legacy platform mapping behind an explicit compatibility helper

**Files:**
- Create: `src/Agibuild.Fulora.Adapters.Abstractions/WebViewLegacyAdapterCompatibility.cs`
- Modify: `src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterRegistry.cs`
- Modify: `src/Agibuild.Fulora.Runtime/WebViewAdapterFactory.cs`
- Create: `tests/Agibuild.Fulora.UnitTests/WebViewLegacyAdapterCompatibilityTests.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/WebViewAdapterRegistryTests.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/BranchCoverageRound3Tests.cs`

- [ ] **Step 1: Write the failing compatibility tests**

Create `tests/Agibuild.Fulora.UnitTests/WebViewLegacyAdapterCompatibilityTests.cs` with tests that require:

```csharp
[Fact]
public void GetCurrentPlatform_matches_the_legacy_platform_expected_by_current_runtime()

[Fact]
public void GetCandidatesForCurrentPlatform_returns_only_matching_legacy_registrations()

[Fact]
public void Registry_and_branch_coverage_tests_can_reuse_the_shared_compatibility_mapping()
```

Update `BranchCoverageRound3Tests.cs` so it no longer carries its own `GetCurrentPlatformForTest()` clone.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewLegacyAdapterCompatibilityTests|FullyQualifiedName~WebViewAdapterRegistryTests|FullyQualifiedName~BranchCoverageRound3Tests"
```

Expected: FAIL because the compatibility helper does not exist and the test-only platform mapping is still duplicated.

- [ ] **Step 3: Implement the explicit compatibility boundary**

Add `WebViewLegacyAdapterCompatibility` as an internal helper that owns only:

- current-platform mapping for the legacy enum path
- selection/projection of legacy registrations for the current platform
- any small test seam needed so unit tests stop cloning runtime OS logic

Update `WebViewAdapterRegistry` so:

- provider candidates stay provider-first
- legacy registrations are obtained only through the compatibility helper
- the provider path no longer depends on a shared `GetCurrentPlatform()` switch
- provider resolution never routes through `WebViewLegacyAdapterCompatibility`

Update `WebViewAdapterFactory` only as needed to keep its role narrow:

- load candidate assemblies
- ask the registry/resolver pipeline for an adapter
- throw the existing `PlatformNotSupportedException` when nothing resolves
- if any provider-path OS branching is discovered during implementation, remove it instead of relocating it into the compatibility helper

Do not rewrite module initializers. Do not add a second discovery pipeline. The compatibility helper exists only to quarantine the legacy path until it can be retired later.

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewLegacyAdapterCompatibilityTests|FullyQualifiedName~WebViewAdapterRegistryTests|FullyQualifiedName~BranchCoverageRound3Tests"
```

Expected: PASS

- [ ] **Step 5: Run the mandatory regression bundle**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj
dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj
dotnet test Agibuild.Fulora.sln --no-restore -m:1
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Adapters.Abstractions/WebViewLegacyAdapterCompatibility.cs src/Agibuild.Fulora.Adapters.Abstractions/WebViewAdapterRegistry.cs src/Agibuild.Fulora.Runtime/WebViewAdapterFactory.cs tests/Agibuild.Fulora.UnitTests/WebViewLegacyAdapterCompatibilityTests.cs tests/Agibuild.Fulora.UnitTests/WebViewAdapterRegistryTests.cs tests/Agibuild.Fulora.UnitTests/BranchCoverageRound3Tests.cs
git commit -m "$(cat <<'EOF'
Isolate WebView legacy adapter compatibility from provider resolution

Constraint: keep the provider path free of centralized legacy platform branching while preserving fallback behavior.
Rejected: shared-layer provider OS switch | deleting legacy compatibility before the resolver contract is stable
Confidence: medium
Scope-risk: narrow
Reversibility: clean
Directive: quarantine legacy platform mapping behind an explicit compatibility helper and keep the factory focused on assembly loading.
Tested: dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter \"FullyQualifiedName~WebViewLegacyAdapterCompatibilityTests|FullyQualifiedName~WebViewAdapterRegistryTests|FullyQualifiedName~BranchCoverageRound3Tests\"; dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj; dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj; dotnet test Agibuild.Fulora.sln --no-restore -m:1
Not-tested: manual platform smoke on Windows/macOS/iOS/Android/Gtk

EOF
)"
```

---

### Task 3: Remove the redundant shell callback from lifecycle runtime ownership

**Only do this task if the Task 3 trigger above is still true after the platform-selection slices are green. It does not depend on changing legacy compatibility code; it depends only on the control-side ownership review still finding the redundant callback smell.**

**Files:**
- Modify: `src/Agibuild.Fulora.Avalonia/WebViewControlLifecycleRuntime.cs`
- Modify: `src/Agibuild.Fulora.Avalonia/WebView.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/WebViewControlLifecycleRuntimeTests.cs`

- [ ] **Step 1: Write the failing lifecycle tests**

Extend `WebViewControlLifecycleRuntimeTests.cs` with tests that require:

```csharp
[Fact]
public void AttachToNativeControl_success_does_not_depend_on_a_shell_adapter_callback()

[Fact]
public void AttachToNativeControl_platform_not_supported_marks_runtime_unavailable_without_a_shell_adapter_callback()
```

Implement the tests by passing a callback that throws if invoked. The current code should fail because `WebViewControlLifecycleRuntime` still calls `setAdapterUnavailable` even though production wiring in `WebView.cs` passes a no-op.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewControlLifecycleRuntimeTests"
```

Expected: FAIL because the lifecycle runtime still depends on the redundant shell callback.

- [ ] **Step 3: Implement the minimal ownership cleanup**

Remove the `setAdapterUnavailable` dependency from `WebViewControlLifecycleRuntime`.

Keep the behavior owned by `WebViewControlRuntime`:

- success clears adapter-unavailable state via `AttachCore`
- platform-not-supported uses `MarkAdapterUnavailable`
- other failures use `ClearCore`

Update `WebView.cs` to stop passing the no-op callback. Do not add new shared state. Do not move attach/detach sequencing into `WebViewControlRuntime`.

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewControlLifecycleRuntimeTests|FullyQualifiedName~WebViewControlRuntimeTests"
```

Expected: PASS

- [ ] **Step 5: Run the mandatory regression bundle**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj
dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj
dotnet test Agibuild.Fulora.sln --no-restore -m:1
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Avalonia/WebViewControlLifecycleRuntime.cs src/Agibuild.Fulora.Avalonia/WebView.cs tests/Agibuild.Fulora.UnitTests/WebViewControlLifecycleRuntimeTests.cs
git commit -m "$(cat <<'EOF'
Center WebView lifecycle ownership on the control runtime

Constraint: keep lifecycle attach/destroy behavior unchanged while removing redundant shell-only callback state.
Rejected: new control god runtime | moving Avalonia shell concerns into shared lifecycle code
Confidence: medium
Scope-risk: narrow
Reversibility: clean
Directive: let lifecycle runtime depend only on real ownership boundaries and drop the no-op adapter-unavailable callback.
Tested: dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter \"FullyQualifiedName~WebViewControlLifecycleRuntimeTests|FullyQualifiedName~WebViewControlRuntimeTests\"; dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj; dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj; dotnet test Agibuild.Fulora.sln --no-restore -m:1
Not-tested: manual Avalonia host-close smoke on desktop platforms

EOF
)"
```

---

### Task 4: Reduce event-runtime constructor noise with named dependency groupings

**Only do this task if the Task 4 trigger above is still true. If Task 3 was skipped, evaluate Task 4 immediately after Task 2. If the trigger is false, skip Task 4 and stop.**

**Files:**
- Create: `src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntimeBindings.cs`
- Modify: `src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntime.cs`
- Modify: `src/Agibuild.Fulora.Avalonia/WebView.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/WebViewControlEventRuntimeTests.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/WebViewControlLifecycleRuntimeTests.cs`

- [ ] **Step 1: Write the failing construction tests**

Update the event-runtime tests so they construct `WebViewControlEventRuntime` through small named groupings, for example:

```csharp
var runtime = new WebViewControlEventRuntime(
    callbacks: new WebViewControlEventCallbacks(...),
    interactionHandlers: new WebViewControlInteractionAccessors(...),
    navigationHooks: new WebViewControlNavigationHooks(...));
```

Expected failure mode before implementation: the grouping types/constructor overload do not exist yet.

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewControlEventRuntimeTests|FullyQualifiedName~WebViewControlLifecycleRuntimeTests"
```

Expected: FAIL because the new grouped dependency types are not implemented.

- [ ] **Step 3: Implement the minimal grouping**

Add small internal record/struct types that group:

- control event callbacks
- interaction handler accessors
- navigation/zoom hooks

Use the new groupings only to make the constructor readable and test setup smaller. Keep runtime responsibilities unchanged. Do not create interface soup. Do not merge `WebViewControlEventRuntime` into another runtime.

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "FullyQualifiedName~WebViewControlEventRuntimeTests|FullyQualifiedName~WebViewControlLifecycleRuntimeTests"
```

Expected: PASS

- [ ] **Step 5: Run the mandatory regression bundle**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj
dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj
dotnet test Agibuild.Fulora.sln --no-restore -m:1
```

Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntimeBindings.cs src/Agibuild.Fulora.Avalonia/WebViewControlEventRuntime.cs src/Agibuild.Fulora.Avalonia/WebView.cs tests/Agibuild.Fulora.UnitTests/WebViewControlEventRuntimeTests.cs tests/Agibuild.Fulora.UnitTests/WebViewControlLifecycleRuntimeTests.cs
git commit -m "$(cat <<'EOF'
Reduce WebView control event wiring construction noise

Constraint: keep event/runtime behavior identical while making dependency boundaries easier to read and test.
Rejected: interface explosion for small constructor data | folding event runtime back into the Avalonia shell
Confidence: medium
Scope-risk: narrow
Reversibility: clean
Directive: group event-runtime constructor dependencies into small named bindings without changing responsibility boundaries.
Tested: dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter \"FullyQualifiedName~WebViewControlEventRuntimeTests|FullyQualifiedName~WebViewControlLifecycleRuntimeTests\"; dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj; dotnet test tests/Agibuild.Fulora.Governance.UnitTests/Agibuild.Fulora.Governance.UnitTests.csproj; dotnet test Agibuild.Fulora.sln --no-restore -m:1
Not-tested: manual Avalonia interaction smoke on desktop platforms

EOF
)"
```

---

## Stop Conditions

Stop the implementation and do **not** continue to the next task when any of these become true:

- the platform selection chain is provider-first, ordered, explicit about fallback, and the registry no longer acts as both catalog and resolution engine
- the next control-side cleanup would only reduce line count, not improve ownership clarity
- the only remaining logic in `WebView.cs` is Avalonia shell composition plus intentional overlay geometry/math

## Final Review Checklist

- The provider/registry/factory boundary is clear.
- Shared resolution no longer owns hidden platform-selection details beyond explicit legacy compatibility.
- `WebViewControlRuntime` still reads as a core/state owner, not a new orchestration hub.
- No runtime gained extra responsibilities just to make the file graph look cleaner.
- `WebView.cs` still owns Avalonia-only behavior and did not leak into shared runtime/core.
- Every landed slice has focused tests, the mandatory regression bundle, and a Lore-protocol commit.
