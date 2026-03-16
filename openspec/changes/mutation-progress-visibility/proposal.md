## Why

Mutation workflow currently runs as one long Nuke step, so GitHub Actions only shows a single "in progress" state for a long time. When runtime is abnormal, maintainers cannot quickly tell whether execution is progressing, stalled, or which mutation profile is blocking.

## What Changes

- Split mutation CI execution into profile-level units (`core`, `runtime`, `ai`) so progress is visible per profile/job.
- Add profile selector input to Nuke mutation target, while preserving full-run mode for local/weekly usage.
- Emit explicit start/end timestamps and elapsed time per profile in Nuke logs and GitHub step summary.
- Add governance assertions to prevent regression back to a single opaque mutation step.
- Keep mutation scope aligned to core business modules only (no provider-wrapper expansion).

## Non-goals

- Do not redesign mutation scoring policy or thresholds.
- Do not add mutation coverage to non-core integration wrapper projects.
- Do not introduce fallback/compatibility branches that hide real stalls.

## Capabilities

### New Capabilities
- `mutation-testing-progress-observability`: Profile-level mutation execution with machine-visible progress and deterministic status reporting.

### Modified Capabilities
- `build-pipeline-resilience`: Mutation workflow requirements are extended to require progress observability rather than only successful invocation.

## Impact

- Affected systems: `.github/workflows/mutation-testing.yml`, `build/Build.Testing.cs`, mutation governance tests.
- CI behavior: clearer execution timeline, faster triage of stuck profiles, reduced "blind waiting".
- Goal alignment: strengthens **G4** (contract-driven testability/observability) and **G3** (deterministic secure operations), and supports post-Phase-12 maintenance quality gates in `ROADMAP.md`.
