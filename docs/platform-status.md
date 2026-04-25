# Platform Status

## Release-Line Snapshot — 1.6.x

| Field | Value |
|---|---|
| Snapshot date | 2026-04-25 |
| Release line | `1.6.x` (published `1.6.6`) |
| Channel status | Stable |
| Kernel stability | Stable — no breaking changes since `1.0.0`; all `Tier A` capabilities in `framework-capabilities.json` remain `status: stable` |
| Extension readiness | Mixed — `spa.hosting` stable (`Tier B`); `shell.activation` and `bridge.streaming` provisional (`Tier B`); `notification.post` provisional (`Tier C`) |
| Security gate status | Passing — boundary validation + default-deny asserted for every capability in `framework-capabilities.json`; OWASP review bundled into Build and Test on Linux/macOS/Windows; SSL/server-certificate failures route through `INavigationSecurityHooks` with `WebViewSslException` payload — see `docs/API_SURFACE_REVIEW.md` § 1.6 Navigation SSL Policy Explicit. |
| Observability gate status | Passing — every capability exports traces + structured errors; metrics on all `Tier A` + `Tier B` capabilities |
| Release gate status | Passing — `release-gate` job in `.github/workflows/ci.yml` approves only after all three `Build and Test` legs and `Merge Coverage Reports` are green; `release` environment requires explicit human approval before `Publish npm` / `Publish NuGet` / `Create Tag and GitHub Release` fan-out |

Snapshot evidence:

- Release run: [`24787577853`](https://github.com/AGIBuild/Fulora/actions/runs/24787577853) (all publish legs green, v1.6.6 tagged and deployed)
- Capability registry: `docs/framework-capabilities.json` (schema 1.1, updated 2026-03-27)
- API surface: `docs/API_SURFACE_INVENTORY.release.txt` (72 Core + 100 Runtime types, frozen at 1.0 GA)
- API compatibility review: `docs/API_SURFACE_REVIEW.md` (1.6 Capability Split section)

---

## Governed Status Template

This page is the governed status template and publication location for release-line snapshots. Replace the snapshot table above on every release-line advance; keep the tier sections below as the policy reference.

## Tier A

Tier A captures the stable contract required for every supported release line.

- Includes `Kernel` and `Bridge` capabilities marked as stable in `framework-capabilities.json`.
- Current `1.6.x` members: `navigation`, `lifecycle.disposal`, `bridge.binary`, `bridge.cancellation`.
- Changes at this tier require the strongest compatibility evidence and release-gate review (policy: `architecture-approval-required` or `release-gate-required`).
- Compatibility scope and rollback strategy per capability are enforced by the governance test suite against the capability registry.

## Tier B

Tier B captures governed framework and shell experiences that are broadly supported but may evolve faster than the core contract.

- Current `1.6.x` members: `spa.hosting` (stable), `shell.activation` (provisional), `bridge.streaming` (provisional).
- Differences across hosts or front-end stacks must be documented in the registry and release evidence.
- Promotion requires matching framework-level validation and rollback notes.

## Tier C

Tier C captures optional extension lanes and ecosystem add-ons.

- Current `1.6.x` members: `filesystem.read` (stable), `http.outbound` (stable), `notification.post` (provisional).
- Consumers may adopt these selectively, but every declared capability must still publish compatibility scope and rollback strategy.
- Tier C snapshots should call out notable limitations, opt-in requirements, or pending hardening work. Current known limitation: `notification.post` remains provisional pending full cross-platform coverage on desktop hosts.

## Notes

- Replace the `Release-Line Snapshot` table through the regular release process — update on every release-line advance (minor bump), not every patch.
- Keep this document aligned with capability registry updates in `framework-capabilities.json`; the release-gate job verifies both files evolve together.
