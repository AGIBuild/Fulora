## Context

The current pipeline mixes two version authorities (tag/MinVer and release-time overrides) and duplicates readiness logic between `Ci` and `CiPublish`. This weakens determinism and can allow release-time drift from CI-validated state.

This design targets roadmap outcomes already established in:
- Phase 7, M7.1/M7.2 (release evidence consolidation and packaging determinism)
- Phase 11, M11.15 (release automation pipeline)
- Post-phase maintenance continuity governance (`Ci`/`CiPublish` parity model)

It also aligns with `G4` by enforcing machine-checkable, single-contract release readiness and deterministic version provenance.

## Goals / Non-Goals

**Goals:**
- Unify CI and release readiness checks into one authoritative contract.
- Freeze shared baseline version source to `1.5` at repository level.
- Produce CI artifact versions as `X.Y.Z.<run_number>` (no `ci` text suffix).
- Ensure release publishes exactly CI-produced artifacts without rebuilding.
- Make version and artifact provenance machine-verifiable by governance tests.

**Non-Goals:**
- No staged migration; no compatibility bridge for the old tag-driven authority.
- No additional release channels or semantic-version policy redesign beyond this format.
- No runtime/framework feature changes outside build/release governance and workflow orchestration.

## Decisions

### Decision 1: Single readiness contract for both lanes
- **Choice**: Introduce one readiness graph used by both CI and release paths; release only performs publish/promotion with provenance validation.
- **Why**: Eliminates "release-only checks" drift and enforces "CI pass implies release safe."
- **Alternatives considered**:
  - Keep separate `Ci` and `CiPublish` readiness branches: rejected due to parity drift risk.
  - Keep release rebuild: rejected because rebuild breaks reproducibility and can diverge from validated binaries.

### Decision 2: Repository-level base version is fixed at `1.5`
- **Choice**: Set repository shared version baseline (`major.minor`) to `1.5`, then compute patch/revision from CI run metadata.
- **Why**: Gives a single version source for all projects/packages and removes per-workflow ambiguity.
- **Alternatives considered**:
  - Continue MinVer/tag as primary authority: rejected because version is decided too late and depends on tag semantics.
  - Per-project version declaration: rejected due to cross-package inconsistency risk.

### Decision 3: CI computes publishable version `X.Y.Z.<run_number>`
- **Choice**: CI computes package/product version from baseline and run number, then writes a version manifest consumed by release.
- **Why**: Deterministic, monotonic in pipeline context, no prerelease marker noise, and directly reusable by promotion.
- **Alternatives considered**:
  - `X.Y.Z-ci.<run_number>`: rejected by explicit product requirement.
  - Release-time computed version: rejected because it breaks "build once, promote artifact."

### Decision 4: `create-tag.yml` is no longer release version authority
- **Choice**: Decommission `create-tag.yml` as the workflow that decides package version.
- **Why**: Version authority moves to CI manifest + shared baseline; tags become optional traceability metadata only.
- **Alternatives considered**:
  - Keep `create-tag.yml` for version bump and release trigger: rejected due to dual-authority conflict.

## Risks / Trade-offs

- **[Risk] Run number resets or differs across workflow scopes** -> **Mitigation**: constrain release to consume the exact CI run artifact/manifest and validate `version == manifest.version`.
- **[Risk] Ecosystem automation still assumes MinVer/tag precedence** -> **Mitigation**: update governance invariants and fail fast on tag-derived version paths in release.
- **[Risk] One-step cutover introduces temporary process disruption** -> **Mitigation**: implement strict parity tests and rehearsal runs before enabling protected-branch release.
- **[Risk] Version collision in concurrent/manual reruns** -> **Mitigation**: tie artifact identity to run id + commit SHA manifest fields and enforce uniqueness checks in publish gate.

## Migration Plan

1. Apply one-step cutover to workflow and build targets (no dual path).
2. Introduce repository-level version baseline `1.5` and CI version computation contract.
3. Emit immutable CI artifact bundle + version/provenance manifest.
4. Change release workflow to "download + verify + publish" only.
5. Remove `create-tag.yml` from release authority path.
6. Enforce governance invariants and CI tests for readiness/version parity.

## Rollback Plan

- Re-enable previous release workflow entry and old publish path from the last known good commit.
- Disable the new promotion-only gate via workflow rollback commit.
- Preserve produced artifacts for forensic comparison; no package unpublish expected.

## Testing Strategy

- **Contract tests (CT)**: update governance/unit tests to assert single readiness dependencies, baseline version source, format `X.Y.Z.<run_number>`, and release manifest parity.
- **Integration tests (IT)**: run `Ci` lane to produce artifacts/manifest, then run release lane in promotion mode to validate no rebuild and successful publish simulation.
- **Workflow verification**: validate GitHub workflow job graph ensures release cannot proceed without successful CI readiness evidence.
- **MockBridge**: not applicable for this change domain; no bridge runtime behavior is modified.

## Open Questions

- Should tags remain as optional release annotations only, or be fully removed from automation triggers in this repository policy?
