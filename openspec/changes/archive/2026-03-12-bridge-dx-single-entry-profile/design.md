## Context

Fulora runtime and generator already provide the core primitives for deterministic host bootstrap, typed bridge contracts, and handshake-based readiness. However, sample/template app layers still show multiple patterns (manual `NavigateAsync + Expose`, direct `window.agWebView.rpc`, handwritten service typing, inconsistent mock usage), which creates avoidable DX drift.

This change hardens post-roadmap-maintenance quality by converging DX on one architecture path, aligned with:
- **G1 / G2 / G4** (typed bridge, SPA hosting, contract-driven testability),
- **E1 / E2 / E3** (template, tooling ergonomics, HMR-friendly flows),
- roadmap continuity from **Phase 10 M10.2** (DI/bootstrap productization) and **Phase 11 M11.14 + M11.13** (HMR/readiness + diagnostics quality).

The design remains consistent with `docs/agibuild_webview_design_doc.md`: contract-first, runtime semantic ownership, and mock-testable behaviors.

## Goals / Non-Goals

**Goals:**
- Provide a single, profile-based DX entrypoint for host startup and web bridge usage.
- Make generated bridge artifacts the normative contract surface in app layers.
- Standardize standalone browser development via generated mock profile path.
- Preserve handshake-first readiness semantics while keeping explicit compatibility fallback.
- Add governance that enforces semantic DX invariants (not fragile folder/file shape assumptions).

**Non-Goals:**
- Replacing JSON-RPC transport or bridge runtime fundamentals.
- Rewriting platform adapters or shell subsystem internals.
- Requiring migration of archived/unmaintained historical demos outside the official sample set.

## Decisions

### D1. Introduce profile-based single-entry architecture
Define a framework-level profile contract where app code uses one approved host path and one approved web path.

- Host side: profile wrapper over existing bootstrap semantics (navigation + registration + lifecycle).
- Web side: profile wrapper over bridge client semantics (ready + middleware + optional mock install).

**Why:** removes duplicated glue logic and reduces app-layer semantic drift.  
**Alternative rejected:** keep “multiple valid patterns” and rely on docs alone; rejected because governance cannot guarantee convergence.

### D2. Generated artifacts are normative for app-layer service contracts
App layers in templates/samples consume generated `bridge.client.ts` + `bridge.d.ts` (+ `bridge.mock.ts` for browser-only mode). Handwritten bridge DTO/service contracts become exception-only.

**Why:** enforces single source of truth and avoids contract drift.  
**Alternative rejected:** continue mixed generated+handwritten patterns; rejected due to long-term maintenance cost.

### D3. Handshake-first readiness through profile API
Readiness is consumed via profile API that resolves sticky state/event first and only uses polling as explicit compatibility fallback.

**Why:** eliminates race-prone ad-hoc polling loops in app code while preserving backward compatibility.  
**Alternative rejected:** hard-disable polling immediately; rejected to avoid abrupt breakage in legacy usage.

### D3.1 Profile API package location is fixed to `@agibuild/bridge/profile`
Profile APIs are exported from a dedicated package subpath (`@agibuild/bridge/profile`) rather than root `@agibuild/bridge`.

**Why:** keeps root package surface minimal and stable, makes profile adoption explicit, and improves tree-shaking and long-term API governance.  
**Alternative rejected:** root-level exports for profile APIs; rejected to avoid root surface inflation and conceptual mixing between low-level primitives and opinionated app profile helpers.

### D4. Browser-standalone development profile using generated mock
Define deterministic browser-only startup mode that installs generated `bridge.mock.ts` before app mount, so frontend can run without native host.

**Why:** improves frontend iteration speed and keeps mock behavior aligned with generated contracts.  
**Alternative rejected:** custom per-sample mock stubs; rejected because they reintroduce duplication.

### D5. Governance as semantic invariants with scoped exceptions
Add invariant checks for app-layer anti-patterns (direct low-level bridge plumbing in app code) with explicit allowlist for framework internals and compatibility shims.

**Why:** prevents style drift without breaking legitimate low-level runtime/package code.  
**Alternative rejected:** broad text grep bans; rejected as brittle and noisy.

### D6. Immediate migration policy for official samples
All official maintained samples, including complex samples currently using direct low-level RPC patterns, migrate in this change scope without a compatibility grace period.

**Why:** avoids prolonged dual-style operation and enforces architectural convergence where developer onboarding impact is highest.

## Risks / Trade-offs

- **[Risk] Immediate migration can surface hidden regressions in complex samples** → **Mitigation:** expand CT/IT parity coverage and require sample-level build/test gates before closeout.
- **[Risk] Governance false positives on low-level modules** → **Mitigation:** invariant targets app-layer paths and uses explicit exemption metadata.
- **[Risk] API surface expansion in bridge package** → **Mitigation:** keep profile APIs composable, minimal, and layered over existing public contracts.
- **[Risk] Migration churn in templates/samples** → **Mitigation:** strict per-sample acceptance criteria with deterministic rollback path at feature-flag/gate level.

## Migration Plan

1. Add profile contracts (`@agibuild/bridge/profile`) while keeping low-level APIs for framework/internal advanced usage.
2. Migrate `templates/agibuild-hybrid` defaults to profile entrypoints + generated artifacts.
3. Immediately migrate all official maintained samples (including complex direct-RPC sample paths) to profile path.
4. Enable governance checks as enforcing gates for official sample/template app-layer paths in this change.
5. Close with strict semantic governance and zero sample-level direct low-level bridge startup wiring.

Rollback: preserve low-level bootstrap/client APIs for framework/internal usage, but keep official sample/template profile invariants as release gate requirements; rollback occurs by reverting this change set, not by reintroducing dual official DX paths.

## Testing Strategy

- **Contract Tests (CT):** profile readiness semantics (late/early subscribers), generated artifact consumption invariants, mock-profile determinism.
- **Integration Tests (IT):** host profile bootstrap ordering/lifecycle, template startup behavior, browser-only mock startup behavior.
- **MockBridge/MockAdapter:** verify app-layer flows remain testable without real browser/native host.
- **Governance tests:** semantic invariant diagnostics include invariant id, path, and expected-vs-actual payload.

## Open Questions

- None at design level; API location and migration strictness are now fixed by decision.
