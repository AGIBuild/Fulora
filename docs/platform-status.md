# Platform Status

## Current Snapshot

This page tracks the current docs-first governed platform snapshot for the active Fulora release line.

- Snapshot date: `2026-04-01`
- Release line: `0.2.x docs-first governance baseline`
- Stable channel status: `Pre-stable release governance baseline`
- Capability registry version: `1.0-seeded`
- Security gate status: `Capability policy taxonomy documented; runtime policy evaluator still in P2`
- Observability gate status: `Bridge-first diagnostics shipped; runtime/framework unification still in P3`
- Release gate status: `Docs-first release governance is active for roadmap, closeout snapshot, and layering checks`

## Tier A

Stable cross-platform contract capabilities:

- `kernel.navigation`: stable on Windows/macOS/Linux, preview on iOS/Android.
- `kernel.lifecycle.disposal`: stable on Windows/macOS/Linux, preview on iOS/Android.
- `bridge.transport.binary`: stable on desktop hosts, preview on mobile hosts.
- `bridge.transport.cancellation`: stable on desktop hosts, preview on mobile hosts.
- `bridge.transport.streaming`: stable on desktop hosts, preview on mobile hosts.

## Tier B

Officially supported capabilities with documented platform variation:

- `framework.spa.hosting`: stable on Windows/macOS, documented Linux variation, mobile support planned.
- `framework.shell.integration`: stable on Windows/macOS, documented Linux variation, mobile support planned.
- `framework.shell.activation`: stable on Windows/macOS, documented Linux variation, mobile support planned.

## Tier C

Experimental capabilities without a stable cross-platform SLA:

- `plugin.filesystem.read`: experimental and policy-gated.
- `plugin.http.outbound`: experimental and policy-gated.
- `plugin.notification.post`: experimental and policy-gated.
- `plugin.ai.integration`: experimental and opt-in.

## Known Limitations

- Linux remains primarily a dialog-first support story; embedded desktop parity is not part of the current baseline.
- Android and iOS stay preview for Tier A bridge/kernel capabilities and are not yet Tier B-ready for framework services.
- Capability policy enforcement for plugin powers is not fully implemented yet; the registry entries are the current contract seed for P2 hardening.
- Platform-specific caveats must continue to flow from `framework-capabilities.json` into release evidence and support messaging.

## Notes

- Keep this page aligned with `release-governance.md` and `framework-capabilities.json`.
- Support claims must use this page plus the machine-readable registry together.
- This page is a governed snapshot, not a replacement for machine-readable release evidence.
