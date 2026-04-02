# Release Governance

## Stable Release Rules

1. Stable releases must preserve kernel compatibility contracts unless architecture approval is recorded.
2. Capability lifecycle changes (`provisional` -> `stable`, deprecations, removals) require evidence updates in `framework-capabilities.json`.
3. Security and observability controls are mandatory gates for promotion.
4. Breaking capability changes must satisfy each capability's `breakingChangePolicy`, and release-gate evidence is mandatory.
5. Any failed gate blocks release promotion until remediation evidence is published.

## Release Gates

| Gate | Required Evidence | Block Condition |
|---|---|---|
| Compatibility | Kernel/API diff review + approval record | Unapproved breaking change |
| Capability Registry | Updated `framework-capabilities.json` entries | Missing or stale capability metadata |
| Security | Security review report + boundary validation checks | Unresolved critical/high security findings |
| Observability | Trace/metric/error baseline report | Missing baseline or regression beyond threshold |
| Documentation | Top-level docs presence and link governance tests | Required platform docs not discoverable |
| Quality | Targeted unit/integration/e2e governance test pass | Any required governance suite failing |

## Machine-Readable Evidence

Release promotion is gated by repository evidence under `artifacts/test-results/` rather than roadmap phase markers or legacy archived spec entries.

- `artifacts/test-results/closeout-snapshot.json` is the canonical release closeout snapshot and must include provenance, test, coverage, and governance sections.
- `artifacts/test-results/transition-gate-governance-report.json` verifies the `Ci` lane keeps the required release gates in its dependency closure.
- `artifacts/test-results/release-orchestration-decision-report.json` is the final publish/no-publish decision payload.
- `artifacts/test-results/distribution-readiness-governance-report.json` and `artifacts/test-results/adoption-readiness-governance-report.json` extend the closeout snapshot with release-specific readiness findings.

## Promotion Flow

1. Prepare candidate and refresh capability + status snapshots.
2. Run governance and release gate checks.
3. Generate or refresh the closeout snapshot and release-orchestration decision artifacts.
4. Approve or reject promotion with machine-readable evidence.
5. Publish stable release only when all gates pass.
