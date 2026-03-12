## Why

Current `Ci` and `CiPublish` do not use a single release-readiness contract, and version assignment is split between MinVer/tag flow and release-time overrides. This can cause non-deterministic release behavior and weakens the "CI passed -> Release is safe" guarantee.

This change aligns with `G4` (contract-driven determinism) and roadmap post-phase maintenance priorities after Phase 12, while reinforcing Phase 7/11 release-orchestration outcomes.

## What Changes

- Introduce a unified readiness path shared by CI and release, with release reusing CI-validated artifacts.
- Freeze the solution version source to major/minor baseline `1.5` (single source of truth at repo level).
- Define CI build version format as `X.Y.Z.<run_number>` without `ci` suffix text.
- Ensure all packable outputs (NuGet and npm package version) are derived from the same computed version in one pipeline execution.
- Remove dependence on tag-time version computation for release publishing; release consumes the CI-produced version manifest and artifacts.
- **BREAKING**: Decommission tag-driven version bumping as release authority (`create-tag.yml` no longer controls package versioning).

## Capabilities

### New Capabilities
- `ci-release-version-governance`: Deterministic versioning and artifact-promotion contract where CI computes version and release only promotes verified artifacts.

### Modified Capabilities
- `governance-semantic-assertions`: Extend governance invariants to validate unified readiness, version source consistency, and release artifact/version parity.

## Impact

- Affected workflows: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, and release triggering strategy.
- Affected build orchestration: `build/Build.cs`, `build/Build.Governance.cs`, and packaging/version injection points.
- Affected version configuration: repository-level shared version properties (baseline `1.5`) and CI run-number-based patch increment.
- Affected governance/testing: update invariants and tests for readiness/version parity.

## Non-goals

- No partial migration stage; this is a one-step cutover.
- No backward compatibility with old tag-bump release authority.
- No additional release channels beyond the unified CI-computed version strategy in this change.
