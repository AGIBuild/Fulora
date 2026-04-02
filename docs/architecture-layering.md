# Architecture Layering

## Four Layers

- `Kernel`
- `Bridge`
- `Framework Services`
- `Plugins / Vertical Features`

## Dependency Policy

The dependency policy is enforced through repository-visible governance hooks. Any layering violation must point developers back to this document before merge.

## Allowed Dependencies

- `Kernel` depends only on BCL and internal kernel contracts.
- `Bridge` may depend on `Kernel` but must not depend on `Framework Services` or `Plugins / Vertical Features`.
- `Framework Services` may depend on `Kernel` and `Bridge` but must not redefine kernel or bridge semantics.
- `Plugins / Vertical Features` may depend on `Kernel`, `Bridge`, and `Framework Services`, but must remain outside the kernel boundary.

## Allowed Public API Types

- `Kernel API`: WebView lifecycle, dispatcher, dialog, auth broker, navigation, messaging, cancellation, exception, and baseline security semantics.
- `Bridge API`: typed export and import contracts, generators, streaming, transport, binary payload, and tracing abstractions.
- `Framework API`: SPA hosting, shell activation, deep linking, window shell coordination, telemetry integration, and auto-update orchestration.
- `Plugin API`: optional feature and vertical integration contracts such as filesystem, HTTP, database, notifications, auth token, biometric, local storage, or AI.

## Capability Classification Decision Tree

1. Does the capability define host-neutral runtime invariants or baseline security semantics?
   - Yes: classify it as `Kernel`.
2. Does the capability define typed bridge execution, source generation, or transport semantics?
   - Yes: classify it as `Bridge`.
3. Does the capability coordinate shell, hosting, deep linking, updates, or telemetry on top of kernel and bridge contracts?
   - Yes: classify it as `Framework Services`.
4. Does the capability expose vertical integrations, optional plugins, or showcase-only features?
   - Yes: classify it as `Plugins / Vertical Features`.
5. If none apply, keep the capability internal until the owning layer is clear.

## Kernel API Approval Rules

- Any new public `Kernel API` requires architecture approval before merge.
- Any breaking `Kernel API` change requires architecture approval before merge and again before stable release promotion.
- Any semantic change to lifecycle, security, cancellation, navigation, or messaging invariants is treated as a kernel change even if signatures do not move.
- Every kernel approval record must include dependency impact, compatibility plan, rollback plan, and linked release-gate evidence.
