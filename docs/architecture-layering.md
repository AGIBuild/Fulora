# Architecture Layering

Fulora governs four platform layers: `Kernel`, `Bridge`, `Framework`, and `Plugin`.
`Product Surface` sits above these layers as an application concern, but it is not part of the platform dependency envelope.

## Four Layers

- `Kernel` — runtime contracts, execution invariants, lifecycle primitives, and policy boundaries shared across hosts.
- `Bridge` — JS/C# contract generation, transport semantics, serialization rules, and bridge-facing diagnostics.
- `Framework` — UI host integration, platform adapters, framework bootstrapping, and shell-facing composition.
- `Plugin` — optional capability packages that extend the platform without introducing reverse dependencies into lower layers.

## Dependency Policy

- `Kernel` depends only on BCL and internal kernel contracts.
- `Bridge` may depend on `Kernel`, but never on `Framework` or `Plugin`.
- `Framework` may depend on `Kernel` and `Bridge`, but never on `Plugin`.
- `Plugin` may depend on `Kernel` and `Bridge`, but never on `Framework` or other plugins.
- Reverse dependencies from lower layers into higher layers are forbidden.
- `build/Build.LayeringGovernance.cs` is the first automated check for these boundaries and must point reviewers back to this document when it fails.

## Allowed Public API Categories

- `Kernel API` — core lifecycle, scheduling, and invariant enforcement.
- `Bridge API` — bridge contracts, serialization boundaries, and source-generation surfaces.
- `Framework API` — host integration, adapter composition, and shell-facing abstractions.
- `Plugin API` — optional capability extensions with explicit support-tier labels.

## Classification Decision Tree

1. Does the API define runtime invariants used across hosts/frameworks?
   - Yes: classify as `Kernel API`.
2. Does the API define JS/C# contract, bridge code generation, or transport semantics?
   - Yes: classify as `Bridge API`.
3. Does the API bind to framework or host-specific behavior?
   - Yes: classify as `Framework API`.
4. Does the API ship as an optional extension capability?
   - Yes: classify as `Plugin API`.
5. If none apply, keep internal and do not publish.

## Kernel API Architectural Approval Rule

- Any new public `Kernel API`, breaking `Kernel API` change, or semantic behavior change requires architecture approval before merge.
- Approval must include:
  - dependency-boundary impact statement,
  - compatibility plan,
  - rollback/fallback plan,
  - linked governance evidence for release gates.
