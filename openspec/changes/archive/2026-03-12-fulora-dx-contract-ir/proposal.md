## Why

Fulora currently delivers type-safe bridge contracts, but developer workflows still duplicate the same contract across C# interfaces, hand-written TypeScript services, browser mock stubs, and host bootstrap code. This creates drift risk, high ceremony, and delayed runtime-only failures, especially when DTOs and parameter names evolve.

This change advances **G1** (type-safe bridge) and **G4** (contract-driven testability), and aligns with post-Phase-12 **post-roadmap-maintenance** hardening by consolidating duplicated DX surfaces into a single contract source. It also strengthens prior roadmap outcomes from **Phase 10 / 10.2** (DI integration) and **Phase 11 / 11.13, 11.14** (error diagnostics + HMR resilience).

## What Changes

- Add a framework-level **Bridge Contract IR** as the single source for generated bridge artifacts.
- Generate typed TS client artifacts (DTO typings + service proxies + mock stubs) from the same IR, reducing hand-written `services.ts` and `index.html` mocks.
- Introduce host bootstrap conventions for deterministic SPA navigation + bridge registration + lifecycle wiring.
- Replace polling-based bridge readiness with sticky handshake semantics (state + event), eliminating timing races.
- Normalize bridge error semantics end-to-end (structured code/message/data), removing string-based error channels and silent failure patterns.
- Productize DI/plugin registration as default bridge-host wiring path without reflection-first assembly scanning.

## Architecture Scope and Guardrails

- Treat the contract IR as the only semantic source for all bridge artifacts (`bridge.d.ts`, `bridge.client.ts`, `bridge.mock.ts`, host metadata).
- Centralize CLR-to-TypeScript semantics in IR-driven mapping; emitter-local string parsing is compatibility-only and not normative for new behavior.
- Make framework bootstrap API own dev/prod orchestration (including production SPA hosting enablement), deterministic registration ordering, and lifecycle ownership.
- Make handshake readiness (sticky state + event) the normative path; keep polling only as explicit compatibility fallback.
- Shift governance from rigid file-shape assertions to behavior-contract assertions where migration requires structural evolution.

## Non-goals

- Replacing JSON-RPC transport protocol or introducing a new IPC stack.
- Re-architecting platform adapter internals unrelated to bridge DX contracts.
- Delivering C# hot-reload state resurrection in this change (tracked separately).

## Capabilities

### New Capabilities
- `bridge-contract-ir-pipeline`: Define and enforce a canonical contract IR used by all bridge artifact emitters.
- `bridge-host-bootstrap`: Provide framework-owned host bootstrap flow for SPA navigation, bridge registration, and lifecycle binding.
- `bridge-ready-handshake`: Define sticky ready semantics for web clients (state + event) to replace polling-only readiness.

### Modified Capabilities
- `bridge-typescript-generation`: Expand from declaration-focused output to IR-backed typed client + DTO + mock artifact generation.
- `bridge-js-middleware`: Strengthen middleware/error requirements for structured bridge error propagation and global handling hooks.
- `webview-di-integration`: Formalize DI/plugin-first bridge service exposure as recommended default integration path.

## Impact

- **Generator**: `Agibuild.Fulora.Bridge.Generator` (IR model + emitters).
- **Bridge package**: `packages/bridge` (`ready` semantics, error normalization contracts).
- **Runtime/Core/DI**: host bootstrap APIs and DI/plugin wiring conventions.
- **Samples/Templates**: reduced host and web ceremony; generated mock + typed client adoption.
- **Tests**: contract/unit/integration coverage updates for generation determinism, readiness semantics, and error consistency.

## Delivery Sequencing

1. Contract Kernel hardening (IR + deterministic invariants).
2. Artifact semantic parity across declaration/client/mock outputs.
3. Bootstrap convergence (framework-owned navigation/registration/lifecycle).
4. Ready handshake normalization and API consistency.
5. Sample/template migration to generated artifacts and bootstrap conventions.
6. Governance decoupling and closeout validation.

Downstream stages are blocked if upstream architecture invariants are not met.
