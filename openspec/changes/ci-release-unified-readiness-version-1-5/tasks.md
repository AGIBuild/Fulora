## 1. Version authority cutover (Deliverables D1/D2)

- [ ] 1.1 [D1] Update shared repo version source to baseline `1.5` in central build properties and remove conflicting tag-first version authority inputs (AC: all packable projects resolve baseline major/minor from one source).
- [ ] 1.2 [D2] Implement CI version computation utility producing `X.Y.Z.<run_number>` and wire it into pack/publish version inputs (AC: computed version is numeric four-part and contains no `ci` text suffix).

## 2. Build orchestration and provenance manifest (Deliverables D3/D4)

- [ ] 2.1 [D3] Refactor build targets so CI and release depend on one readiness contract graph with no release-only quality checks (AC: dependency graph parity is machine-checkable).
- [ ] 2.2 [D4] Add CI artifact provenance manifest generation containing version, commit SHA, run id, and artifact hashes (AC: manifest is uploaded with CI artifact bundle and consumed by release lane).

## 3. Workflow one-step cutover (Deliverables D5/D6)

- [ ] 3.1 [D5] Update `.github/workflows/ci.yml` to publish immutable promotable artifact bundle + manifest after readiness success (AC: release-required bundle is always produced by CI).
- [ ] 3.2 [D5] Update `.github/workflows/release.yml` to perform download + verify + publish only, with explicit no-rebuild policy (AC: release workflow contains no package build/pack step).
- [ ] 3.3 [D6] Remove `create-tag.yml` from release version authority path (decommission or convert to metadata-only trigger role) (AC: release version is sourced exclusively from CI manifest path).

## 4. Governance assertions and test coverage (Deliverables D7)

- [ ] 4.1 [D7] Extend governance invariants for version provenance parity and no-rebuild promotion policy (AC: deterministic invariant IDs emitted on mismatch).
- [ ] 4.2 [D7] Update unit/automation governance tests for shared readiness dependencies, baseline `1.5`, and `X.Y.Z.<run_number>` format checks (AC: tests fail on drift and pass on compliant graph).
- [ ] 4.3 [D7] Add integration verification for CI artifact -> release promotion path reuse (AC: promotion flow proves version equality and artifact hash continuity).
