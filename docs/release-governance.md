# Release Governance

Fulora release governance is docs-first. The source of truth is this document plus the machine-readable evidence generated under `artifacts/test-results/`. Release automation must not depend on OpenSpec roadmap files or OpenSpec archive folders.

## Stable Release Rules

1. Stable releases preserve kernel and bridge compatibility unless architecture approval is recorded.
2. Stable support claims must match the latest capability snapshot and platform status publication.
3. Breaking capability changes must satisfy each capability's `breakingChangePolicy` before promotion.
4. Security and observability evidence are required for every stable release candidate.
5. Any failed gate blocks promotion until remediation evidence is published and verified.

## Release Gates

| Gate | Required Evidence | Block Condition |
| --- | --- | --- |
| Compatibility | API diff review plus architecture approval when required | Unapproved breaking kernel or bridge change |
| Capability Snapshot | Updated `framework-capabilities.json` and linked status sections | Missing, stale, or inconsistent capability metadata |
| Security | Security review, boundary validation evidence, and deny-policy checks | Unresolved critical or high-risk findings |
| Observability | Baseline traces, metrics, and structured error evidence | Missing baseline or unapproved regression |
| Package Smoke | Packaging and install verification for the governed release set | Install, launch, or package validation failure |
| Auto-Update Smoke | Update detection, download, and apply verification for supported channels | Update flow fails or rollback path is unverified |
| Documentation | Governance docs present and discoverable in DocFX navigation | Required platform documents are missing or hidden |
| Quality | Targeted governance, unit, integration, and E2E evidence | Required release suites are failing |

## Evidence Contract and Artifact Convention

- Evidence schema: every gate artifact must include `gate`, `releaseLine`, `snapshotAtUtc`, `status`, `producer`, and `artifacts[]` with machine-readable details.
- Docs-first evidence baseline: `docs/release-governance.md` defines the release policy, and `artifacts/test-results/closeout-snapshot.json` records the current closeout snapshot used by build/release governance.
- Closeout snapshot contract: the snapshot must record `provenance`, `transition`, `tests`, `coverage`, `governance`, and `closeoutSummary` so release lanes can validate the current docs-first governance state.
- Governance document linkage: release evidence must point back to `docs/release-governance.md` instead of any OpenSpec artifact.
- Field semantics (required for each gate artifact record):
  - `gate`: the release gate identity the record belongs to (for example `Compatibility`, `Security`, `Quality`).
  - `releaseLine`: the governed release line this evidence is proving (for example `1.0.x` or `1.1.0-rc`).
  - `snapshotAtUtc`: the UTC snapshot timestamp for the evidence record, formatted as ISO-8601 (`YYYY-MM-DDTHH:MM:SSZ`).
  - `status`: gate outcome at snapshot time (`pass`, `fail`, `blocked`, or equivalent controlled status values used by release automation).
  - `producer`: the generator identity that produced this evidence (CI target, workflow job, or release tool name).
  - `artifacts[]`: machine-readable artifact descriptors; each item must include at least `type`, `path`, and an integrity/reference value (`hash` or immutable build/run id).
- Evidence path: release evidence is stored under `artifacts/releases/<release-line>/<gate>/`.
- Naming convention: use `<release-line>.<gate>.<artifact-kind>.<timestamp-utc>.json` where timestamp uses `yyyyMMddTHHmmssZ`.
- Snapshot linkage: capability evidence must include the `framework-capabilities.json` version and the corresponding `platform-status.md` snapshot section reference.
- Release closeout linkage: the closeout snapshot must include the governed documentation path and confirm docs-first governance artifacts are present before promotion.
- Retention rule: keep the full stable promotion evidence set and preserve hashes for reproducibility checks.

## Promotion Flow

1. Prepare the release candidate and refresh the capability snapshot plus platform status.
2. Run release gates and collect machine-readable evidence for each required category.
3. Review blocked gates, approve only when all required evidence is complete, and reject otherwise.
4. Publish the stable release only after every required gate is green and linked evidence is retained.
