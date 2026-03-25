# Fulora Platform Roadmap Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the OpenSpec-based roadmap/governance system with a `docs/`-first platform definition, then align repository docs, build gates, and tests to the new model without leaving broken links or stale governance checks behind.

**Architecture:** The migration proceeds in layers. First add the new normative platform documents and machine-readable capability placeholders, then redirect public documentation to those sources, then remove OpenSpec assets and all build/test/workflow dependencies on them, and finally re-baseline governance tests around the new documentation model. This keeps the repository valid at every checkpoint and avoids a long broken intermediate state.

**Tech Stack:** Markdown, JSON, DocFX docs, Nuke build targets, xUnit governance tests, ripgrep, git

---

### Task 1: Add the Replacement Platform Documents

**Files:**
- Create: `docs/product-platform-roadmap.md`
- Create: `docs/architecture-layering.md`
- Create: `docs/platform-status.md`
- Create: `docs/framework-capabilities.json`
- Create: `docs/release-governance.md`
- Modify: `docs/toc.yml`
- Modify: `docs/index.md`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Write the failing documentation governance tests**

Create `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs` with assertions that:

- `docs/product-platform-roadmap.md` exists
- `docs/architecture-layering.md` exists
- `docs/platform-status.md` exists
- `docs/framework-capabilities.json` exists
- `docs/release-governance.md` exists
- `docs/index.md` links to `product-platform-roadmap.md`
- `docs/toc.yml` includes the new top-level docs pages

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: FAIL because the new docs and test file do not exist yet.

- [ ] **Step 3: Add the new platform documents with minimal but valid content**

Author the first complete versions:

- `docs/product-platform-roadmap.md`
  - positioning
  - strategic direction
  - stable core vs extensions
  - layering model
  - capability support contract
  - security model
  - observability model
  - release governance
  - developer defaults
  - `P0` to `P5` roadmap
  - documentation governance
- `docs/architecture-layering.md`
  - allowed dependencies
  - allowed public API categories
  - new capability classification decision tree
  - kernel API architectural approval rule
- `docs/platform-status.md`
  - current snapshot placeholder tied to the new docs model
- `docs/framework-capabilities.json`
  - starter schema with a few representative capability entries
- `docs/release-governance.md`
  - stable release rules and release gate requirements

Update:

- `docs/toc.yml` so the new docs pages are reachable in DocFX
- `docs/index.md` so the new platform docs are first-class entry points

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docs/product-platform-roadmap.md docs/architecture-layering.md docs/platform-status.md docs/framework-capabilities.json docs/release-governance.md docs/toc.yml docs/index.md tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs
git commit -m "docs: add platform roadmap and governance docs"
```

### Task 2: Repoint Public Docs to the New Platform Definition

**Files:**
- Modify: `README.md`
- Modify: `docs/index.md`
- Modify: `docs/articles/architecture.md`
- Modify: `docs/articles/bridge-guide.md`
- Modify: `docs/articles/spa-hosting.md`
- Modify: `docs/shipping-your-app.md`
- Modify: `docs/release-checklist.md`
- Modify: `docs/agibuild_webview_design_doc.md`
- Modify: `docs/docs-site-deploy.md`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Extend the failing tests for docs link migration**

Add assertions that the governed public docs no longer contain:

- `openspec/ROADMAP.md`
- `openspec/PROJECT.md`
- `openspec/specs/`

Add assertions that:

- `README.md` links to `docs/product-platform-roadmap.md`
- `README.md` no longer makes roadmap claims through deleted OpenSpec files
- `docs/shipping-your-app.md` points to `docs/release-governance.md`

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: FAIL because current docs still reference OpenSpec paths.

- [ ] **Step 3: Update the public-facing docs**

Make the following edits:

- `README.md`
  - remove OpenSpec roadmap/project links
  - add link to `docs/product-platform-roadmap.md`
  - soften uniform platform-parity wording in favor of capability tiers
- `docs/index.md`
  - remove roadmap table that references OpenSpec
  - add links to `product-platform-roadmap.md` and `platform-status.md`
- `docs/articles/architecture.md`
  - replace OpenSpec references with the new docs pages
  - align "runtime core" wording with the four-layer model
- `docs/articles/bridge-guide.md`
  - replace roadmap links with the new platform roadmap doc
- `docs/articles/spa-hosting.md`
  - replace roadmap links with the new platform roadmap doc
- `docs/shipping-your-app.md`
  - replace OpenSpec spec link with `docs/release-governance.md`
- `docs/release-checklist.md`
  - add the new governance doc as the normative reference
- `docs/agibuild_webview_design_doc.md`
  - remove OpenSpec references or reframe it as historical background
- `docs/docs-site-deploy.md`
  - remove the note that OpenSpec link warnings are expected

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: PASS

- [ ] **Step 5: Run a repo-wide search to confirm OpenSpec links are gone from public docs**

Run:

```bash
rg -n "openspec/ROADMAP.md|openspec/PROJECT.md|openspec/specs/" README.md docs
```

Expected: no matches outside intentionally historical or migration plan files.

- [ ] **Step 6: Commit**

```bash
git add README.md docs/index.md docs/articles/architecture.md docs/articles/bridge-guide.md docs/articles/spa-hosting.md docs/shipping-your-app.md docs/release-checklist.md docs/agibuild_webview_design_doc.md docs/docs-site-deploy.md tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs
git commit -m "docs: repoint public docs to platform roadmap"
```

### Task 3: Remove OpenSpec Workflow Assets and PR Requirements

**Files:**
- Delete: `openspec/`
- Delete: `.github/skills/openspec-ff-change/SKILL.md`
- Delete: `.github/skills/openspec-sync-specs/SKILL.md`
- Delete: `.github/skills/openspec-continue-change/SKILL.md`
- Delete: `.github/skills/openspec-bulk-archive-change/SKILL.md`
- Delete: `.github/skills/openspec-onboard/SKILL.md`
- Delete: `.github/skills/openspec-archive-change/SKILL.md`
- Delete: `.github/skills/openspec-new-change/SKILL.md`
- Delete: `.github/skills/openspec-verify-change/SKILL.md`
- Delete: `.github/skills/openspec-explore/SKILL.md`
- Delete: `.github/skills/openspec-apply-change/SKILL.md`
- Delete: `.github/prompts/opsx-archive.prompt.md`
- Delete: `.github/prompts/opsx-onboard.prompt.md`
- Delete: `.github/prompts/opsx-explore.prompt.md`
- Delete: `.github/prompts/opsx-apply.prompt.md`
- Delete: `.github/prompts/opsx-new.prompt.md`
- Delete: `.github/prompts/opsx-sync.prompt.md`
- Delete: `.github/prompts/opsx-bulk-archive.prompt.md`
- Delete: `.github/prompts/opsx-continue.prompt.md`
- Delete: `.github/prompts/opsx-ff.prompt.md`
- Delete: `.github/prompts/opsx-verify.prompt.md`
- Modify: `.github/PULL_REQUEST_TEMPLATE.md`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Extend tests for repository workflow cleanup**

Add assertions that:

- `openspec/` no longer exists
- `.github/PULL_REQUEST_TEMPLATE.md` no longer mentions OpenSpec artifacts
- `.github/skills/openspec-*` and `.github/prompts/opsx-*` are absent

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: FAIL because the OpenSpec files still exist.

- [ ] **Step 3: Remove the workflow assets and update the PR template**

Delete:

- the entire `openspec/` directory
- all `.github/skills/openspec-*`
- all `.github/prompts/opsx-*`

Update `.github/PULL_REQUEST_TEMPLATE.md`:

- remove `OpenSpec artifacts created for non-trivial changes`
- add `Layer Impact` under summary or changes

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add .github/PULL_REQUEST_TEMPLATE.md .github/skills .github/prompts tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs
git add -A openspec
git commit -m "chore: remove openspec workflow assets"
```

### Task 4: Remove OpenSpec Build Targets and Re-Baseline Release Governance

**Files:**
- Delete: `build/Build.Governance.OpenSpec.cs`
- Modify: `build/Build.Governance.Release.cs`
- Modify: `build/Build.cs`
- Modify: `tests/Agibuild.Fulora.UnitTests/AutomationLaneGovernanceTests.cs`
- Modify: `docs/release-governance.md`
- Test: `tests/Agibuild.Fulora.UnitTests/AutomationLaneGovernanceTests.cs`

- [ ] **Step 1: Write or update failing governance tests first**

Update `tests/Agibuild.Fulora.UnitTests/AutomationLaneGovernanceTests.cs` so it asserts the new expected state:

- build sources no longer declare `OpenSpecStrictGovernance`
- `Ci` and related lanes no longer depend on `OpenSpecStrictGovernance`
- release closeout snapshot logic no longer reads `openspec/ROADMAP.md`
- release closeout snapshot no longer reports `openSpecStrictGovernance`
- tests do not require `openspec` archive paths or roadmap markers

- [ ] **Step 2: Run the focused governance tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "AutomationLaneGovernanceTests|DocumentationGovernanceTests"
```

Expected: FAIL because build sources still reference OpenSpec.

- [ ] **Step 3: Remove the OpenSpec build target and update release governance logic**

Edit:

- `build/Build.Governance.OpenSpec.cs`
  - delete the file
- `build/Build.cs`
  - remove `OpenSpecStrictGovernanceReportFile`
- `build/Build.Governance.Release.cs`
  - remove target dependencies on `OpenSpecStrictGovernance`
  - remove parity rules that require `OpenSpecStrictGovernance`
  - remove reads of `openspec/ROADMAP.md`
  - remove archive-directory inspection under `openspec/changes/archive`
  - remove `openSpecStrictGovernance` fields from closeout snapshot payload and report checks
  - replace any phase/transition constant logic with the new `docs/`-based governance model or delete it if no replacement is ready in this phase
- `docs/release-governance.md`
  - make its rules match the new build behavior

- [ ] **Step 4: Re-run the focused governance tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "AutomationLaneGovernanceTests|DocumentationGovernanceTests"
```

Expected: PASS

- [ ] **Step 5: Run a targeted build command**

Run:

```bash
./build.sh --target ReleaseCloseoutSnapshot
```

Expected: PASS without any OpenSpec executable or path dependency.

- [ ] **Step 6: Commit**

```bash
git add build/Build.Governance.Release.cs build/Build.cs docs/release-governance.md tests/Agibuild.Fulora.UnitTests/AutomationLaneGovernanceTests.cs
git add -A build/Build.Governance.OpenSpec.cs
git commit -m "build: remove openspec governance targets"
```

### Task 5: Add Layering Governance and PR Review Hooks

**Files:**
- Modify: `docs/architecture-layering.md`
- Modify: `.github/PULL_REQUEST_TEMPLATE.md`
- Modify: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`
- Create: `build/LayeringGovernance.targets` or `build/Build.LayeringGovernance.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Add a failing test for layering governance visibility**

Add assertions that:

- `docs/architecture-layering.md` contains the four layers and a dependency policy section
- `.github/PULL_REQUEST_TEMPLATE.md` contains `Layer Impact`
- a repo-visible layering-governance file exists under `build/`

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: FAIL if `Layer Impact` or layering-governance build file is not present.

- [ ] **Step 3: Implement the minimal layering governance hook**

Choose one minimal enforcement mechanism and wire it in:

- Option A: `build/Build.LayeringGovernance.cs`
  - a Nuke target that scans namespace dependencies with `rg`
- Option B: `build/LayeringGovernance.targets`
  - an MSBuild target that blocks forbidden namespace references

Keep this first version intentionally small:

- detect forbidden reverse dependencies
- fail with a human-readable message
- point developers to `docs/architecture-layering.md`

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docs/architecture-layering.md .github/PULL_REQUEST_TEMPLATE.md tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs
git add -A build/Build.LayeringGovernance.cs build/LayeringGovernance.targets
git commit -m "ci: add layering governance hook"
```

### Task 6: Seed the Capability Registry and Current Status Model

**Files:**
- Modify: `docs/framework-capabilities.json`
- Modify: `docs/platform-status.md`
- Modify: `docs/product-platform-roadmap.md`
- Modify: `docs/agibuild_webview_compatibility_matrix_proposal.md`
- Modify: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`

- [ ] **Step 1: Add failing tests around the capability registry structure**

Add assertions that:

- `framework-capabilities.json` contains required keys
- at least one capability exists for each of `Kernel`, `Bridge`, `Framework`, and `Plugin`
- `platform-status.md` references Tier A, Tier B, and Tier C
- `product-platform-roadmap.md` references `framework-capabilities.json` and `platform-status.md`

- [ ] **Step 2: Run the focused tests to verify they fail**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: FAIL until the registry/status docs are filled in.

- [ ] **Step 3: Fill in the first useful capability registry**

Populate `docs/framework-capabilities.json` with representative entries such as:

- `navigation`
- `lifecycle.disposal`
- `bridge.binary`
- `bridge.cancellation`
- `bridge.streaming`
- `spa.hosting`
- `shell.activation`
- `filesystem.read`
- `http.outbound`
- `notification.post`

Then update:

- `docs/platform-status.md`
  - current version snapshot
  - Tier A summary
  - Tier B differences
  - Tier C limitations
- `docs/product-platform-roadmap.md`
  - link to the capability registry and status page
- `docs/agibuild_webview_compatibility_matrix_proposal.md`
  - add a pointer that the proposal is historical and the current contract lives in the new registry and status docs

- [ ] **Step 4: Re-run the focused tests**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter DocumentationGovernanceTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add docs/framework-capabilities.json docs/platform-status.md docs/product-platform-roadmap.md docs/agibuild_webview_compatibility_matrix_proposal.md tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs
git commit -m "docs: seed capability registry and platform status"
```

### Task 7: Full Verification and Documentation Build

**Files:**
- Modify: any files required to fix final verification failures
- Test: `tests/Agibuild.Fulora.UnitTests/DocumentationGovernanceTests.cs`
- Test: `tests/Agibuild.Fulora.UnitTests/AutomationLaneGovernanceTests.cs`

- [ ] **Step 1: Run the full unit-test governance subset**

Run:

```bash
dotnet test tests/Agibuild.Fulora.UnitTests/Agibuild.Fulora.UnitTests.csproj --filter "DocumentationGovernanceTests|AutomationLaneGovernanceTests"
```

Expected: PASS

- [ ] **Step 2: Run docs build or link validation**

Run:

```bash
dotnet docfx docs/docfx.json
```

Expected: PASS

- [ ] **Step 3: Run a final repository search for OpenSpec remnants**

Run:

```bash
rg -n "openspec|OpenSpecStrictGovernance|opsx-" . --glob '!docs/superpowers/**' --glob '!.git/**'
```

Expected: no matches in active repository code, docs, tests, workflows, or templates.

- [ ] **Step 4: Review the git diff for unintended deletions**

Run:

```bash
git diff --stat
git diff -- .github build docs README.md tests
```

Expected: changes only reflect the planned migration.

- [ ] **Step 5: Commit final cleanup**

```bash
git add -A
git commit -m "refactor: complete docs-first platform governance migration"
```

## Notes for the Implementer

- Keep `docs/superpowers/specs/2026-03-26-fulora-product-platform-roadmap-design.md` open while executing; it is the normative design source for this plan.
- Do not preserve partial OpenSpec compatibility. The goal is complete removal.
- When removing OpenSpec-related tests, prefer rewriting them to assert the new intended state instead of deleting governance coverage outright.
- If a command fails because the repo uses a different documented build lane, change the command in the plan before implementation rather than improvising mid-task.
