## 1. Profile Contract Foundation

- [x] 1.1 Define host/web single-entry profile contracts and option models in runtime + bridge package, with web profile exports at `@agibuild/bridge/profile` (Deliverable: `bridge-dx-single-entry-profile`; Acceptance: one normative host entrypoint and one normative web entrypoint are available and profile imports resolve from subpath).
- [x] 1.2 Add explicit extension-hook and exception-scope metadata model for advanced low-level customization (Deliverable: `bridge-dx-single-entry-profile`; Acceptance: advanced path requires explicit opt-in and can be identified by governance).

## 2. Host Bootstrap Profile Integration

- [x] 2.1 Implement host bootstrap profile wrapper over existing bootstrap semantics for deterministic dev/prod path orchestration (Deliverable: `bridge-host-bootstrap`; Acceptance: profile API selects dev server or embedded hosting deterministically).
- [x] 2.2 Implement deterministic DI-first + explicit bridge registration composition in profile host bootstrap (Deliverable: `bridge-host-bootstrap`; Acceptance: integration test confirms stable ordering across repeated runs).
- [x] 2.3 Implement profile-owned lifecycle teardown with idempotent disposal behavior (Deliverable: `bridge-host-bootstrap`; Acceptance: teardown tests prove exactly-once release without manual per-service dispose wiring).

## 3. Readiness and Middleware Profile Convergence

- [x] 3.1 Implement handshake-first readiness profile API that resolves sticky state/event before compatibility polling (Deliverable: `bridge-ready-handshake`; Acceptance: CT covers early and late subscriber success paths without race).
- [x] 3.2 Keep compatibility polling as explicit fallback with deterministic timeout semantics and diagnostics (Deliverable: `bridge-ready-handshake`; Acceptance: fallback timeout behavior is deterministic and test-covered).
- [x] 3.3 Extend middleware profile defaults to include structured error normalization hooks in baseline setup path (Deliverable: `bridge-js-middleware`; Acceptance: normalized error hook receives structured error context before rethrow).

## 4. Generated Artifact and Template/Sample Adoption

- [x] 4.1 Enforce generated contract artifacts as normative sample/template app-layer consumption surface (Deliverable: `bridge-typescript-generation`; Acceptance: official maintained template/sample bridge services import from generated artifacts by default).
- [x] 4.2 Update hybrid template web and desktop startup to profile entrypoint conventions (Deliverable: `project-template`; Acceptance: template scaffolds profile-based startup and removes ad-hoc app-layer bridge polling wiring).
- [x] 4.3 Immediately migrate all official maintained samples (including complex low-level direct RPC paths) to profile startup and generated-contract path (Deliverable: `bridge-dx-single-entry-profile`; Acceptance: official sample startup/build/tests pass with profile conventions and app-layer direct low-level startup plumbing removed).

## 5. Governance and Verification

- [x] 5.1 Add semantic governance invariants for app-layer prohibited bridge plumbing with strict enforcement on official sample/template app-layer paths (Deliverable: `governance-semantic-assertions`; Acceptance: governance emits invariant id + path + expected/actual diagnostics and blocks non-compliant official sample/template paths).
- [x] 5.2 Add CT/IT coverage for profile readiness, host ordering/lifecycle, and standalone-browser mock startup behavior (Deliverable: `bridge-dx-single-entry-profile`; Acceptance: new CT/IT suites pass and cover profile core flows).
- [x] 5.3 Run full validation (`openspec validate --all --strict`, targeted package tests, and repository test gate) and capture closeout evidence (Deliverable: governance closeout; Acceptance: all commands pass with no new validation failures).
