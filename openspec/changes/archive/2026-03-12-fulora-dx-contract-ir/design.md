## Context

Bridge DX currently requires developers to maintain the same contract across multiple surfaces: C# interfaces, handwritten TS service wrappers, browser mock stubs, and host startup wiring. This violates contract-first principles and causes drift. Existing architecture already has strong foundations (source generator, JSON-RPC transport, plugin registration, middleware pipeline), but generation outputs and host orchestration are not unified.

Roadmap alignment:
- **G1** and **G4** from `openspec/PROJECT.md` (typed bridge + contract-driven testability).
- Hardening continuation in **post-roadmap-maintenance**, building on:
  - **Phase 10 / 10.2** (`AddFulora()` DI integration),
  - **Phase 11 / 11.13** (enhanced bridge diagnostics),
  - **Phase 11 / 11.14** (bridge continuity across reload workflows).

Architecture consistency with `docs/agibuild_webview_design_doc.md`:
- Keep contract-first approach.
- Keep runtime semantic ownership in framework APIs, not sample code.
- Keep testability via CT + MockAdapter/MockBridge style contracts.

## Goals / Non-Goals

**Goals:**
- Introduce a canonical Bridge Contract IR and make it the single source for bridge artifact generation.
- Generate typed TS client artifacts (DTO declarations + service proxies + mock stubs) from the same IR.
- Define host bootstrap orchestration as framework-owned deterministic flow (navigation, registration, lifecycle).
- Replace polling-only readiness with sticky handshake semantics.
- Preserve structured bridge error semantics (`code/message/data`) through middleware and caller boundaries.
- Make DI/plugin registration the default bridge exposure path without reflection-first scanning.

**Non-Goals:**
- Replacing JSON-RPC transport or introducing a new wire protocol.
- Large platform adapter rewrites unrelated to bridge DX contracts.
- Implementing C# runtime hot-reload state resurrection in this change.
- Forcing immediate migration of all samples/templates in one PR (migration is staged).

## Decisions

### D1: Add canonical Bridge Contract IR in generator

Create a normalized contract model (services, methods, params, DTO graph, events, error metadata) in `Agibuild.Fulora.Bridge.Generator` and make all emitters consume this model.

**Why:** prevents emitter-specific drift and allows deterministic multi-target generation.

**Alternative considered:** continue extending `TypeScriptEmitter` directly. Rejected because it keeps generation logic fragmented and hard to validate.

### D2: Generate multi-artifact TS output from one IR

Generate:
- declaration artifact (`bridge.d.ts` equivalent contract types),
- typed runtime client artifact (`bridge.client.ts`),
- deterministic browser mock artifact (`bridge.mock.ts`).

**Why:** removes hand-written `services.ts`/`index.html` mock duplication while preserving current bridge package contract style.

**Alternative considered:** schema-first external IDL. Rejected for now due to migration and adoption overhead.

### D3: Add host bootstrap API instead of sample-owned orchestration

Provide framework bootstrap API for:
- dev/release SPA navigation policy,
- bridge registration order guarantees,
- disposal/lifecycle binding.

**Why:** moves orchestration semantics into framework, reduces repeated sample boilerplate, and preserves deterministic timing.

**Alternative considered:** `ExposeAll(assembly)` reflection scan. Rejected due to AOT/trim risk and implicit registration behavior.

### D4: Sticky ready handshake replaces polling-only readiness

Ready semantics become two-channel:
- sticky ready state readable at any time,
- ready event for reactive listeners.

Client `ready()` resolves immediately when state is already ready; otherwise subscribes to handshake event with timeout.

**Why:** eliminates race conditions where event can fire before listener registration.

### D5: Structured error semantics are normative

Bridge JS runtime and middleware must preserve structured error fields (`code/message/data`) when available. String-only `"Error: ..."` paths are treated as legacy and non-compliant for new bridge service contracts.

**Why:** enables deterministic diagnostics, retry policy, and consistent UI handling.

### D6: DI/plugin-first bridge exposure is default guidance

Formalize plugin/service-provider bridge exposure flow as first-class path in DI integration and host bootstrap.

**Why:** existing capability exists but is not productized; this closes the gap without introducing reflection-heavy alternatives.

## Architecture Invariants (Non-Negotiable)

1. **Single Source of Truth**
   - All bridge artifacts (`bridge.d.ts`, `bridge.client.ts`, `bridge.mock.ts`, host metadata) MUST be emitted from `BridgeContractModel`.
   - No emitter may re-discover contract shape from raw Roslyn symbols or string parsing.

2. **Type Semantics Canonicalization**
   - Type mapping logic MUST be centralized in one IR-based mapper.
   - String-based CLR type parsing is compatibility-only and MUST NOT drive new artifact semantics.

3. **Bootstrap Ownership**
   - `BootstrapSpaAsync` MUST own dev/prod orchestration, including production SPA hosting enablement.
   - App hosts provide configuration, not orchestration logic.

4. **Ready Contract Priority**
   - Handshake (sticky state + event) is normative.
   - Polling is fallback-only and must be explicitly marked compatibility behavior.

5. **Behavior-over-Shape Governance**
   - Governance checks must validate runtime behavior/contracts, not fixed file names or template folder shape.

## Execution Order (Critical Path)

- Phase A: Contract Kernel hardening (IR + deterministic invariants)
- Phase B: Artifact parity (declaration/client/mock from one semantic path)
- Phase C: Bootstrap convergence (framework-owned navigation + bridge lifecycle)
- Phase D: Ready handshake normalization (API consistency + race safety)
- Phase E: Sample/template migration
- Phase F: Governance decoupling and final closeout

No downstream phase may complete while upstream invariants are violated.

## Risks / Trade-offs

- **[Risk] Migration churn in samples/templates** → **Mitigation:** staged migration with compatibility shims and focused acceptance tests.
- **[Risk] Generator complexity growth** → **Mitigation:** IR schema invariants + snapshot tests per generated artifact.
- **[Risk] Error contract mismatch across layers** → **Mitigation:** contract tests that assert `code/message/data` preservation from runtime to middleware.
- **[Risk] Bootstrap API overreach** → **Mitigation:** keep API minimal and composable; no platform-specific UI semantics in core bootstrap contract.

## Migration Plan

1. Introduce Contract IR and keep current `bridge.d.ts` output backward compatible.
2. Add typed client/mock generation and integrate optional consumption path in samples.
3. Introduce host bootstrap API and DI/plugin-first registration helpers.
4. Migrate template + key samples to generated client/mock and bootstrap API.
5. Remove legacy sample-only polling and string-error patterns after compatibility window.

Rollback: preserve existing bridge client invocation path and old generated declaration contract until migration acceptance criteria are met.

## Open Questions

- Should generated `bridge.client.ts` be emitted into app projects directly or packaged through `@agibuild/bridge` build tooling by default?
- For mock generation, do we require deterministic static payload scaffolds only, or optional scenario-aware mock behaviors?

## Testing Strategy

- **Contract Tests (CT):** IR invariants, deterministic generation, ready handshake semantics, structured error propagation semantics.
- **Unit Tests (CT-level):** emitter snapshots for declarations/client/mock outputs; DI/plugin default-path tests.
- **Integration Tests (IT):** host bootstrap flow with real navigation + bridge exposure timing; sample-level no-polling readiness behavior.
- **MockBridge/MockAdapter coverage:** all normative behaviors testable without real browser dependency where possible.
