# Fulora Product Platform Roadmap Design

## Summary

This design defines how Fulora should evolve from a feature-rich hybrid toolkit into a product-grade hybrid application platform with clearer boundaries, explicit platform guarantees, policy-governed capabilities, unified observability, and enforced release governance.

It also retires the current OpenSpec-based documentation and workflow model in favor of a `docs/`-first system that is easier to read externally and easier to keep consistent internally.

## Goals

- Re-center Fulora on its core identity: `Avalonia + WebView + Typed Bridge + Policy + Tooling + Shipping`.
- Establish a new public-facing platform document that becomes the primary product and roadmap entry point.
- Define a layered architecture model that prevents further expansion of the kernel boundary.
- Replace broad cross-platform claims with an explicit capability tier contract.
- Evolve security from WebMessage-level rules to framework-level capability governance.
- Converge bridge diagnostics, runtime diagnostics, and framework diagnostics into one observability model.
- Move release quality from documentation guidance to hard release governance.
- Remove the OpenSpec documentation, commands, prompts, and governance hooks entirely.

## Non-Goals

- This design does not define per-file implementation edits for runtime code.
- This design does not replace API reference documentation.
- This design does not attempt to preserve any OpenSpec compatibility layer.
- This design does not expand Fulora into an AI-first platform.

## Problem Statement

Fulora already contains the right foundational ingredients for a durable hybrid app framework:

- Strong WebView contract semantics
- Runtime plus adapter architecture
- Typed bridge with source generation and AOT safety
- Tooling for diagnostics, packaging, and shipping

The current risk is no longer capability absence. The risk is that too many responsibilities are still being described, documented, and in some places implemented as one large "runtime core" story. At the same time, public platform promises are broader than the documented maturity of several capabilities, and project status is spread across multiple documentation systems that have already drifted.

## Design Decisions

### 1. Public Platform Definition Moves to `docs/`

Fulora will adopt a `docs/`-first platform definition model.

The new primary public-facing platform document will be:

- `docs/product-platform-roadmap.md`

This document will become the main external entry point for:

- product positioning
- architecture boundaries
- platform capability guarantees
- security and observability direction
- high-level roadmap phases

`README.md` and `docs/index.md` will link to it rather than restating roadmap and support details in full.

### 2. OpenSpec Is Fully Retired

The repository will fully remove the OpenSpec system.

This includes:

- the entire `openspec/` directory
- `.github/skills/openspec-*`
- `.github/prompts/opsx-*`
- build and release governance code that depends on OpenSpec files or OpenSpec validation
- repository documentation links that point to `openspec/ROADMAP.md`, `openspec/PROJECT.md`, or `openspec/specs/*`
- PR checklist items that require OpenSpec artifacts

There will be no compatibility bridge and no retained archive mechanism under a new name. The replacement model is smaller:

- normative platform docs in `docs/`
- machine-readable capability metadata
- CI gates tied to tests and generated status artifacts

### 3. New Documentation System

The new documentation system will separate platform definition, architecture rules, machine-readable capability facts, current status, and release governance.

#### 3.1 Core documents

- `docs/product-platform-roadmap.md`
  - public-facing platform definition and roadmap
- `docs/architecture-layering.md`
  - layering rules, allowed dependencies, API boundary rules, classification decision tree
- `docs/platform-status.md`
  - current platform snapshot for the latest release line
- `docs/framework-capabilities.json`
  - machine-readable capability registry
- `docs/release-governance.md`
  - stable-release rules and release gate policy

#### 3.2 Existing documents that remain

- `README.md`
  - entry point, quick start, concise capability summary
- `docs/index.md`
  - documentation portal and navigation
- `docs/articles/architecture.md`
  - explanatory architecture article
- `docs/release-checklist.md`
  - operational release procedure
- `docs/API_SURFACE_REVIEW.md`
  - API review and freeze reference

#### 3.3 Existing documents that change role

- `docs/agibuild_webview_compatibility_matrix_proposal.md`
  - remains temporarily as historical reference until capability data is absorbed into the new registry and status pages
- `docs/agibuild_webview_design_doc.md`
  - remains historical; no longer the primary high-level platform definition

### 4. Product Positioning Is Narrowed and Stabilized

The new platform definition will explicitly state:

- Fulora is a product-grade hybrid application platform for .NET and Avalonia.
- Fulora is not an AI-first framework.
- AI remains a vertical or optional integration layer, not the platform identity.
- Fulora is not just a WebView wrapper; its value is the contract model around WebView, bridge, policy, tooling, and shipping.

Recommended stable positioning statement:

> Fulora is a product-grade hybrid application platform for .NET and Avalonia, centered on WebView contracts, typed bridge execution, policy-governed capabilities, and production shipping workflows.

### 5. The Architecture Model Is Reduced to Four Layers

The platform will be documented through four architectural layers:

- Kernel
- Bridge
- Framework Services
- Plugins / Vertical Features

#### 5.1 Kernel

Kernel responsibility:

- `IWebView`
- `IWebDialog`
- `IWebAuthBroker`
- dispatcher and adapter host abstractions
- navigation, lifecycle, messaging, cancellation, exception, and baseline security semantics
- minimal diagnostics hooks

Kernel must not absorb:

- SPA hosting
- shell activation
- deep-link orchestration
- theme and window shell services
- auto-update
- vertical integrations such as database, HTTP, filesystem, notifications, auth token, biometric, local storage, or AI

#### 5.2 Bridge

Bridge responsibility:

- `[JsExport]` and `[JsImport]`
- source generators
- bridge runtime services
- transport and protocol
- cancellation, streaming, binary payload, overload boundary behavior
- bridge tracer abstraction

Bridge must not become a generic application service container.

#### 5.3 Framework Services

Framework Services responsibility:

- SPA hosting
- shell activation
- deep-link orchestration
- theme and window shell coordination
- auto-update integration
- telemetry integration

Framework Services may build on Kernel and Bridge, but may not redefine kernel semantics.

#### 5.4 Plugins / Vertical Features

Plugins and vertical features responsibility:

- filesystem
- HTTP client
- database
- notifications
- auth token
- biometric
- local storage
- AI integrations
- IDE tooling
- showcase or demo-only capabilities

Plugins must remain outside the kernel boundary even when officially maintained.

### 6. Layering Rules Become Explicit

`docs/architecture-layering.md` will define:

- which layers may depend on which other layers
- which kinds of public API each layer may expose
- a decision tree for classifying new capabilities
- a policy that kernel API changes require explicit architectural approval

The initial namespace and packaging direction will be documented as logical layering first, not mandatory assembly breakup:

- `Agibuild.Fulora.Kernel.*`
- `Agibuild.Fulora.Bridge.*`
- `Agibuild.Fulora.Framework.*`
- `Agibuild.Fulora.Plugin.*`

The first enforcement step may use a Roslyn analyzer or build-time namespace dependency check rather than immediate package restructuring.

### 7. Platform Support Becomes a Capability Contract

Fulora will formally adopt a three-tier capability model.

#### 7.1 Tier A: Baseline

Tier A capabilities:

- are part of Fulora's stable cross-platform contract
- require unified semantics
- require contract tests and integration coverage
- treat unexpected platform differences as defects

Representative Tier A examples:

- navigation
- lifecycle and disposal
- script invocation
- WebMessage baseline security
- dialog close semantics
- auth callback strict match
- bridge binary, cancellation, and streaming baseline behavior

#### 7.2 Tier B: Extended

Tier B capabilities:

- are officially supported
- may vary by platform
- must document those differences
- must never fail silently

Representative Tier B examples:

- cookies
- context menu
- devtools
- user agent override
- persistent profile
- PDF export

#### 7.3 Tier C: Experimental

Tier C capabilities:

- do not carry a stable cross-platform SLA
- may continue evolving behind explicit documentation
- are not treated as maturity-equivalent to Tier A or Tier B

Representative Tier C example:

- cross-platform cookie APIs with known platform gaps

### 8. Capability Facts Become Machine-Readable

Fulora will introduce a machine-readable capability registry:

- `docs/framework-capabilities.json`

Each capability entry should include at least:

- `capability_id`
- `layer`
- `tier`
- `platform_support`
- `test_requirements`
- `contract_ref`
- `limitations_ref`

This file becomes the factual source for:

- compatibility snapshots
- status page generation
- release artifact summaries
- future CI validation

### 9. Security Evolves from Message Security to Capability Security

Fulora already has a mature WebMessage baseline security model. The next platform step is to govern host and plugin powers with the same rigor.

The capability taxonomy should cover at least:

- `filesystem.read`
- `filesystem.write`
- `filesystem.pick`
- `http.outbound`
- `notification.post`
- `auth.token.read`
- `auth.token.write`
- `shell.external_open`
- `clipboard.read`
- `clipboard.write`
- `window.chrome.modify`

The design direction includes:

- plugin manifest capability declarations
- runtime policy evaluation with `Allow`, `Deny`, and `AllowWithConstraint`
- deny diagnostics with stable schema
- deny-by-default template policy

### 10. Observability Becomes a Runtime Capability

Fulora will converge existing bridge-centric diagnostics into a unified runtime observability model.

The event model will cover:

- Runtime events
- Bridge events
- Framework events

Representative event families:

- `NavigationStarted`
- `NavigationCompleted`
- `NativeNavigationDenied`
- `MessageDropped`
- `DialogClosing`
- `AuthCompleted`
- `ExportCallStart`, `ExportCallEnd`, `ExportCallError`
- `ImportCallStart`, `ImportCallEnd`, `ImportCallError`
- `ServiceExposed`, `ServiceRemoved`
- `SpaHostingModeChanged`
- `HotUpdateApplied`, `HotUpdateRolledBack`
- `AutoUpdateCheck`, `AutoUpdateDownload`, `AutoUpdateApply`
- `CapabilityDenied`
- `PluginLoaded`, `PluginFailed`

The standard event schema should include fields such as:

- `event_name`
- `layer`
- `component`
- `window_id`
- `navigation_id`
- `channel_id`
- `service`
- `method`
- `duration_ms`
- `status`
- `error_type`
- `correlation_id`

`DevTools` should become a consumer of the unified event stream, not a separate tracing universe.

### 11. Release Governance Becomes Mandatory

Fulora will define stable release eligibility through explicit governance rules, not just through guidance.

The release governance model should require:

- platform conformance coverage for baseline flows
- capability snapshot generation
- compatibility summary publication
- package smoke verification
- auto-update smoke verification

Stable release claims must depend on passing release gates, not on manual documentation updates.

### 12. Developer Experience Is Treated as Policy Delivery

CLI and templates should encode the recommended platform model rather than leaving it to documentation discovery.

This includes:

- default project structure aligned to layers
- service generation that requires explicit layer intent
- default diagnostics integration
- default least-privilege capability policy
- generated capability and compatibility placeholders

### 13. Main Public Document Structure

The new `docs/product-platform-roadmap.md` should use this narrative order:

1. Positioning
2. Strategic Direction
3. Stable Core vs Platform Extensions
4. Layering Model
5. Platform Support Contract
6. Security Model
7. Observability Model
8. Release Governance
9. Developer Defaults
10. Execution Roadmap
11. Documentation Governance

This ordering is intentional:

- first define what Fulora is
- then define what belongs to the platform core
- then define what Fulora promises
- then explain how the platform evolves

The document must not become:

- a task tracker
- an API reference
- a marketing landing page

### 14. High-Level Roadmap Expression

The primary roadmap in `docs/product-platform-roadmap.md` will use `P0` through `P5`.

Each phase should use the same template:

- `Objective`
- `Why Now`
- `Major Deliverables`
- `Exit Criteria`

The roadmap summary table should remain compact:

| Priority | Theme | Primary Outcome |
| --- | --- | --- |
| P0 | Kernel Narrowing | Smaller, more defensible core |
| P1 | Capability Contract | Clear support commitments |
| P2 | Capability Security | Policy-governed host capabilities |
| P3 | Runtime Observability | Unified diagnostics across layers |
| P4 | Release Governance | Stable releases with hard gates |
| P5 | DX Defaults | Best practices generated by default |

Detailed task breakdown belongs in later implementation plans, not in the platform definition document.

### 15. Existing Documentation Must Be Simplified

#### 15.1 `README.md`

Keep:

- project positioning summary
- quick start
- concise capability overview
- sample links

Remove or shrink:

- detailed roadmap and phase claims
- direct references to deleted OpenSpec files
- language implying uniform platform parity for all capabilities

Add:

- link to `docs/product-platform-roadmap.md`
- wording that differentiates core runtime model from tiered capability support

#### 15.2 `docs/index.md`

Keep:

- docs navigation
- links to guides and references

Remove:

- roadmap table tied to OpenSpec phase files

Add:

- a clear link to the new platform roadmap document
- a clear link to current platform status

#### 15.3 `docs/articles/architecture.md`

Keep:

- explanation of how runtime, bridge, and capability flows work

Change:

- remove links to deleted OpenSpec roadmap and project files
- align the "runtime core" wording with the new four-layer model

#### 15.4 `docs/shipping-your-app.md`

Change:

- remove references to OpenSpec-based specs
- point governance references to `docs/release-governance.md`

#### 15.5 `docs/release-checklist.md`

Change:

- keep operational steps
- remove OpenSpec-derived governance language
- reference the new release governance policy document

### 16. Repository Workflow Cleanup

The repository will remove OpenSpec workflow remnants from:

- PR template
- build targets
- release governance checks
- docs references
- tests that assert OpenSpec-derived status or paths

The current PR template checklist item:

- `OpenSpec artifacts created for non-trivial changes`

should be deleted rather than replaced with another artifact requirement at this stage.

### 17. Consequences

#### Positive

- Public positioning becomes clearer.
- Platform promises become narrower and more defensible.
- The kernel boundary becomes easier to protect.
- Documentation drift is reduced because status and support claims have explicit homes.
- Release quality becomes easier to reason about as a contract.

#### Negative

- Removing OpenSpec also removes a structured archive/history workflow.
- Some existing governance and release checks must be redesigned or temporarily reduced during migration.
- Documentation migration requires careful link and test cleanup.

#### Risk Mitigation

- Keep the first replacement documentation set intentionally small.
- Make `framework-capabilities.json` the machine-readable fact source early.
- Reduce repetition in `README` and `docs/index.md`.
- Add only governance checks that can be enforced deterministically.

## Execution Plan Shape

The implementation should be split into at least these follow-on workstreams:

1. Documentation system replacement
2. OpenSpec removal
3. Layering rules and enforcement
4. Capability registry and status generation
5. Release governance replacement
6. CLI and template alignment

These workstreams should be planned separately after this design is accepted.

## Acceptance Criteria

- A new public platform roadmap document exists under `docs/`.
- The repository no longer depends on OpenSpec documents, prompts, skills, or build validation.
- The platform definition clearly separates Kernel, Bridge, Framework Services, and Plugins.
- Capability tiers are defined as a release contract.
- Capability security and observability are framed as platform-level models.
- Release governance is described as an enforced rule set, not only as documentation guidance.
- `README.md` and docs landing pages no longer serve as competing sources of roadmap truth.

## Implementation Notes

- Prefer incremental migration of docs rather than rewriting every guide in one pass.
- Remove OpenSpec references and automation in the same implementation phase to avoid broken links and failing gates.
- Land the new platform definition before tightening new release governance so the repository has a clear normative source first.
