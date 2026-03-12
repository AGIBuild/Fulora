## Why

Fulora already has strong bridge/bootstrap primitives, but sample and template apps still use multiple startup and bridge-consumption styles (framework bootstrap, manual navigation, generated contracts, handwritten bridge calls). This drift increases onboarding cost, causes DX inconsistency, and weakens contract-first guarantees in day-to-day development.

This change is needed now to harden post-roadmap-maintenance quality by consolidating Phase 10/11 DX outcomes into one default architecture path.

## What Changes

- Define a **profile-based single-entry DX model** with one recommended path for host startup, web bridge client bootstrapping, and contract consumption.
- Add framework-level **web bridge bootstrap profile API** at `@agibuild/bridge/profile` (for readiness, middleware, and mock/dev fallback) to remove repeated app-level glue while keeping root exports focused.
- Standardize template/sample startup on deterministic host bootstrap + generated bridge artifacts; remove handwritten bridge DTO/service proxy patterns from official defaults.
- Immediately migrate complex official samples that still use low-level direct RPC patterns to the profile path in this change scope.
- Introduce explicit standalone-browser development mode based on generated `bridge.mock.ts` profile conventions.
- Add governance checks that enforce behavior-level DX invariants (single-entry usage, no ad-hoc bridge plumbing in app layer).

## Capabilities

### New Capabilities

- `bridge-dx-single-entry-profile`: Defines profile contracts for host/web/bootstrap entrypoints and standalone browser development.
- `bridge-host-bootstrap`: Defines deterministic host-side bootstrap profile contracts for sample/template startup.
- `bridge-ready-handshake`: Defines handshake-first readiness profile semantics and compatibility boundaries.

### Modified Capabilities

- `bridge-typescript-generation`: Require generated contracts (`bridge.d.ts`, `bridge.client.ts`, `bridge.mock.ts`) as normative DX artifacts for sample/template app layers.
- `project-template`: Change template requirements to scaffold only profile-based bridge startup and generated-contract consumption.
- `bridge-js-middleware`: Require profile-level middleware conventions and deterministic error normalization defaults.
- `governance-semantic-assertions`: Add semantic DX invariants to detect prohibited app-layer bridge plumbing patterns.

## Non-goals

- Replacing JSON-RPC transport or bridge runtime internals.
- Reworking platform adapter implementations.
- Requiring migration of archived/unmaintained historical demos outside the official sample set.

## Impact

- **Runtime/Core**: Host bootstrap and readiness profile entrypoint extensions.
- **Bridge package**: Web bootstrap profile helper and mock-mode conventions.
- **Generator**: Contract artifact consumption expectations and parity governance alignment.
- **Templates/Samples**: Default wiring converges on single-entry profile path.
- **Governance/Tests**: New semantic assertions and migration-safe acceptance coverage.

Alignment:
- Goals: **G1**, **G2**, **G4**, **E1**, **E2**, **E3**.
- Roadmap continuity: post-roadmap-maintenance hardening of Phase 10/11 DX deliverables (notably DI/bootstrap and HMR/readiness workflows).
