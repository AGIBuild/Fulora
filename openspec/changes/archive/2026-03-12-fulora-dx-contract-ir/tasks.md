## 0. Architecture Guardrails

- [x] 0.1 Add architecture invariants to design and enforce IR-only emission policy. Deliverable: all specs; Acceptance: design codifies non-negotiable single-source and ownership rules.
- [x] 0.2 Add acceptance checklist for bootstrap ownership and handshake-first readiness. Deliverable: `bridge-host-bootstrap` + `bridge-ready-handshake`; Acceptance: checklist is explicit and referenced by implementation tasks.
- [x] 0.3 Add governance principle: behavior-contract checks over file-shape checks. Deliverable: governance closeout; Acceptance: governance scope states behavior-first assertions.

## 1. Contract Kernel Foundation

- [x] 1.1 Define canonical Bridge Contract IR types in generator (services, methods, params, DTO graph, events, error metadata). Deliverable: `bridge-contract-ir-pipeline`; Acceptance: generator compiles and IR unit tests validate complete graph extraction.
- [x] 1.2 Refactor all emitters to consume IR exclusively instead of ad-hoc model traversal. Deliverable: `bridge-contract-ir-pipeline`; Acceptance: declaration/client/mock artifacts no longer re-discover contract shape independently.
- [x] 1.3 Add deterministic ordering and naming invariants in IR build step. Deliverable: `bridge-contract-ir-pipeline`; Acceptance: repeated generation on unchanged input produces byte-stable artifacts.

## 2. Artifact Semantic Parity

- [x] 2.1 Centralize TypeRef-to-TypeScript mapping and remove emitter-level string-type divergence. Deliverable: `bridge-typescript-generation`; Acceptance: declaration/client/mock share one semantic type mapping path.
- [x] 2.2 Emit generated typed client artifact (`bridge.client.ts`) whenever service contracts exist, independent from DTO count. Deliverable: `bridge-typescript-generation`; Acceptance: service-only contracts still produce client proxy output.
- [x] 2.3 Emit generated mock artifact (`bridge.mock.ts`) from the same IR semantics and verify declaration/client/mock parity. Deliverable: `bridge-typescript-generation`; Acceptance: snapshot tests assert method and payload shape alignment across artifacts.
- [x] 2.4 Add parity tests for params, cancellation, async enumerable, and structured error typing contracts. Deliverable: `bridge-typescript-generation` + `bridge-js-middleware`; Acceptance: parity tests cover major contract shape paths and pass deterministically.

## 3. Host Bootstrap and DI/Productization

- [x] 3.1 Implement framework bootstrap API that owns dev/prod navigation flow, including production SPA hosting enablement. Deliverable: `bridge-host-bootstrap`; Acceptance: host setup path is reduced to bootstrap call with deterministic mode handling.
- [x] 3.2 Implement deterministic bridge registration ordering in bootstrap pipeline. Deliverable: `bridge-host-bootstrap`; Acceptance: integration tests verify stable registration order across runs.
- [x] 3.3 Add bootstrap-managed lifecycle/disposal ownership for exposed bridge services. Deliverable: `bridge-host-bootstrap`; Acceptance: disposal is idempotent and no manual per-service dispose wiring is required.
- [x] 3.4 Formalize DI/plugin-first bridge exposure path in DI integration helpers. Deliverable: `webview-di-integration`; Acceptance: DI registrations expose bridge services deterministically without reflection-first assembly scan.

## 4. Ready Handshake and Error Semantics

- [x] 4.1 Standardize public JS readiness API contract and keep one normative entrypoint. Deliverable: `bridge-ready-handshake`; Acceptance: sample usage and package entrypoint align on one primary readiness call shape.
- [x] 4.2 Implement sticky ready state + ready event in runtime/client API as primary readiness path. Deliverable: `bridge-ready-handshake`; Acceptance: readiness resolves for both early and late subscribers without race.
- [x] 4.3 Keep polling as compatibility fallback behind handshake-first logic. Deliverable: `bridge-ready-handshake`; Acceptance: legacy callers still function with explicit timeout behavior.
- [x] 4.4 Normalize structured bridge errors (`code/message/data`) end-to-end in JS middleware integration and global hooks. Deliverable: `bridge-js-middleware`; Acceptance: `withErrorNormalization()` preserves structured fields and hook observes normalized errors before rethrow.

## 5. Sample and Template Adoption

- [x] 5.1 Migrate `samples/avalonia-react` to generated typed client and handshake-ready API. Deliverable: `bridge-typescript-generation` + `bridge-ready-handshake`; Acceptance: remove handwritten service-proxy ceremony and polling-first usage.
- [x] 5.2 Migrate `samples/avalonia-ai-chat` to generated mock/bootstrap conventions where applicable. Deliverable: `bridge-host-bootstrap` + `bridge-typescript-generation`; Acceptance: sample host/web startup uses framework-owned orchestration path.
- [x] 5.3 Update template defaults to use generated artifacts and bootstrap conventions. Deliverable: `bridge-host-bootstrap` + `webview-di-integration`; Acceptance: new template project starts with no manual bridge boilerplate.

## 6. Verification and Governance

- [x] 6.1 Add/refresh CT coverage for IR determinism, typed generation parity, ready handshake race safety, and structured error propagation. Deliverable: all specs; Acceptance: new CTs pass and cover edge cases from specs.
- [x] 6.2 Add/refresh IT coverage for host bootstrap ordering and sample startup flows. Deliverable: `bridge-host-bootstrap`; Acceptance: integration suite validates registration timing and lifecycle behavior.
- [x] 6.3 Refactor governance checks from fixed file-shape assertions to behavior-contract assertions where applicable. Deliverable: governance closeout; Acceptance: template and sample governance validates capability behavior, not rigid file presence alone.
- [x] 6.4 Run `openspec validate --all --strict`, targeted package tests, and full repository test gate (`nuke Test`). Deliverable: governance closeout; Acceptance: all commands pass with no validation errors.
