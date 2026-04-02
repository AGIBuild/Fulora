# Product Platform Roadmap

## Positioning

Fulora is a product platform for shipping web-first applications as native desktop products with governed runtime contracts.

## Strategic Direction

- Keep the runtime core host-neutral and stable.
- Push host, framework, and ecosystem change into explicit extension lanes.
- Scale releases through machine-checkable governance artifacts.

## Stable Core vs Extensions

- Stable core: runtime kernel, bridge contracts, lifecycle invariants, security primitives, and diagnostics contracts.
- Extensions: framework adapters, host-specific integration layers, optional plugins, and ecosystem templates.
- Rule: extension velocity must not break stable core compatibility guarantees.

## Layering Model

- `Kernel` — cross-platform runtime contracts and execution invariants.
- `Bridge` — JS/C# contract generation, transport semantics, and bridge diagnostics.
- `Framework` — host integration, adapter composition, shell bootstrapping, and SPA hosting surfaces.
- `Plugin` — optional capability packages that extend the platform without reversing lower-layer dependencies.
- Product surfaces consume these four layers but do not define the platform dependency envelope.

## Capability Support Contract

- Capabilities are registered in `framework-capabilities.json` with explicit lifecycle states.
- Current release-line publication status lives in `platform-status.md`.
- Each capability declares owner, support tier, compatibility scope, and rollback strategy.
- Tier A covers stable `Kernel` and `Bridge` contracts, Tier B covers governed `Framework` surfaces, and Tier C covers optional `Plugin` capabilities.
- Breaking capability changes must follow each capability's `breakingChangePolicy`.
- Architecture approval is mandatory for kernel-level changes and capability policies that explicitly require it.
- release-gate evidence is required for all breaking capability changes.

## Security Model

- Validate all external inputs at the boundary.
- Default-deny privileged operations; require explicit capability enablement.
- Enforce secret isolation through environment/runtime providers only.
- Apply security review gates before stable release promotion.

## Observability Model

- Every capability exposes baseline traces, metrics, and structured error events.
- Release candidates must include machine-readable evidence for health, regression, and fallback readiness.
- Observability artifacts are part of release governance, not optional diagnostics.

## Release Governance

- Stable channel changes require passing release gates defined in `release-governance.md`.
- Kernel API and support-tier changes require architecture review sign-off.
- Regression or policy gate failures block promotion until evidence is updated.

## Developer Defaults

- Safe-by-default templates and policy presets.
- Stable APIs first; extension APIs are opt-in and explicitly marked.
- New capabilities start as provisional until governance evidence is complete.

## P0-P5 Roadmap

| Phase | Focus | Outcome |
|---|---|---|
| P0 | Baseline platform contracts | Kernel and governance envelope established |
| P1 | Layering enforcement | Dependency rules and API categories formalized |
| P2 | Capability registry | Machine-readable capability metadata and support tiers |
| P3 | Security + observability hardening | Required gates and evidence pipelines active |
| P4 | Release automation | Stable release gates fully automated in CI |
| P5 | Ecosystem scale-out | Extension lanes expand without core contract drift |

## Documentation Governance

- Platform docs are first-class governance artifacts and must stay DocFX-discoverable.
- `docs/index.md` and `docs/toc.yml` must expose platform documents as top-level navigation.
- Governance tests enforce presence and linkage for required platform pages.
