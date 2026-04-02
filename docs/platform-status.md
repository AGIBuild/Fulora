# Platform Status

## Governed Status Template

This page is the governed status template and publication location for release-line snapshots.

- Snapshot date: `TBD`
- Channel status: `TBD`
- Kernel stability: `TBD`
- Extension readiness: `TBD`
- Security gate status: `TBD`
- Observability gate status: `TBD`
- Release gate status: `TBD`

## Tier A

Tier A captures the stable contract required for every supported release line.

- Includes `Kernel` and `Bridge` capabilities marked as stable in `framework-capabilities.json`.
- Changes at this tier require the strongest compatibility evidence and release-gate review.
- Current release-line publication fields remain template-driven until snapshot values are promoted.

## Tier B

Tier B captures governed framework and shell experiences that are broadly supported but may evolve faster than the core contract.

- Includes `Framework` capabilities such as SPA hosting and shell activation.
- Differences across hosts or front-end stacks must be documented in the registry and release evidence.
- Promotion requires matching framework-level validation and rollback notes.

## Tier C

Tier C captures optional extension lanes and ecosystem add-ons.

- Includes `Plugin` capabilities such as filesystem, HTTP, and notification extensions.
- Consumers may adopt these selectively, but every declared capability must still publish compatibility scope and rollback strategy.
- Tier C snapshots should call out notable limitations, opt-in requirements, or pending hardening work.

## Notes

- Replace `TBD` values through the release governance process.
- Keep this document aligned with `release-governance.md` and capability registry updates in `framework-capabilities.json`.
