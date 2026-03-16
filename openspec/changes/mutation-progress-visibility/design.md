## Context

Current mutation CI executes `./build.sh --target MutationTest` as one long GitHub Actions step.  
This satisfies single-entry orchestration but creates an observability gap: maintainers cannot see profile-level progress (`core/runtime/ai`) or quickly identify which profile is stalled.  
The issue directly impacts post-Phase-12 operational governance and weakens deterministic CI triage.

## Goals / Non-Goals

**Goals:**
- Make mutation progress machine-visible at profile granularity in GitHub Actions.
- Preserve Nuke as the orchestration authority (no direct `dotnet stryker` in workflow).
- Keep compatibility with current local developer flow (`MutationTest` full run).
- Add governance tests to lock the progress-visible workflow contract.

**Non-Goals:**
- No mutation policy redesign (threshold strategy, score targets).
- No expansion to non-core provider wrapper projects.
- No fallback logic that masks hangs by silently skipping profiles.

## Decisions

1. **Introduce profile selector in Nuke (`MutationProfile`)**
   - Decision: add a parameterized filter so `MutationTest` can run all profiles (default) or one specific profile.
   - Why: keeps one orchestration entrypoint while enabling profile-level execution in CI.
   - Alternative considered: create separate Nuke targets per profile. Rejected because target sprawl duplicates orchestration logic and governance coverage.

2. **Refactor mutation workflow to matrix jobs (`core/runtime/ai`)**
   - Decision: use a strategy matrix and invoke Nuke with `--mutation-profile <name>`.
   - Why: GitHub UI shows independent job state and timing, eliminating opaque single-step waiting.
   - Alternative considered: keep single job and add log heartbeats. Rejected because UI still appears as one long step and triage remains weak.

3. **Emit profile summary into `GITHUB_STEP_SUMMARY`**
   - Decision: Nuke writes profile start/end/elapsed and output path to step summary when available.
   - Why: preserves deterministic, machine-readable progress evidence and improves operator feedback.
   - Alternative considered: rely only on console logs. Rejected due to poor scanability and intermittent log loading issues.

4. **Governance update for progress-visible contract**
   - Decision: extend governance assertions to require matrix-based mutation workflow and profile argument wiring.
   - Why: prevents regressions back to opaque execution.

## Risks / Trade-offs

- **[Risk] More workflow jobs can increase queue/load** → **Mitigation:** keep profile count fixed (3) and reuse existing cache strategy.
- **[Risk] Parameter misuse (`unknown profile`)** → **Mitigation:** strict validation in Nuke and fail-fast message listing allowed values.
- **[Risk] Artifact fragmentation** → **Mitigation:** upload one artifact per profile with deterministic naming, plus optional merged index.

## Migration Plan

1. Add parameterized profile execution and summary emission in `build/Build.Testing.cs`.
2. Update `.github/workflows/mutation-testing.yml` to matrix execution with profile input.
3. Update governance tests for matrix/progress invariants.
4. Validate with targeted governance tests + one local profile smoke run.
5. Push and track remote run; verify per-profile job visibility and deterministic completion.

Rollback: revert workflow to previous single job and remove profile parameter path in Nuke (single commit revert).

## Open Questions

- Should matrix run in parallel (faster feedback) or `max-parallel: 1` (lower runner pressure)?  
  Default proposal: parallel, because visibility and triage speed are priority.
