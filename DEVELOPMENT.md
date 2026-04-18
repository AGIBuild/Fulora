# Developer Guide

This file is the **contributor / maintainer** entry point. End-user docs live in
[`README.md`](README.md), [`docs/articles/getting-started.md`](docs/articles/getting-started.md),
and [`docs/index.md`](docs/index.md).

The goal of this guide: a new contributor can clone, build, run the test suite,
and ship a clean PR within 30 minutes — on any host, with or without mobile
SDKs installed.

---

## Prerequisites

| Requirement | Why |
|---|---|
| **.NET 10 SDK** (see `global.json`) | All projects target `net10.0`. Newer/older SDKs will be rejected by `global.json` rollForward policy. |
| **Node 20+ / npm 10+** | Required for samples that ship a frontend (React / Vue / AI Chat) and for the bridge package. |
| **Git LFS** *(optional)* | Only if you touch large binary fixtures under `tests/`. |
| **Xcode** *(optional, macOS)* | Only required to build/test the iOS adapter. Without it, those projects are skipped automatically. |
| **Android workload** *(optional)* | `dotnet workload install android`. Only required to build/test the Android adapter. |

You **do not** need Xcode or the Android workload to contribute to the core
platform — see "Building without mobile SDKs" below.

---

## The Pre-Commit Ritual

One command, every push, no exceptions:

```bash
./build.sh LocalPreflight
```

This runs, in fail-fast order:

1. `Format`     — `dotnet format --verify-no-changes` (platform-aware solution filter)
2. `Build`      — auto-skips iOS/Android when their workload is missing
3. `UnitTests`  — `Agibuild.Fulora.UnitTests` + `Agibuild.Fulora.Cli.UnitTests`

Wall-clock target: **< 2 minutes** on a warm cache. If it stays green locally,
the corresponding CI gates will almost always stay green too.

If `Format` fails, fix it in one shot:

```bash
dotnet format Agibuild.Fulora.NoMobile.slnf
```

---

## Building Without Mobile SDKs

The default solution `Agibuild.Fulora.slnx` includes `Agibuild.Fulora.Adapters.iOS`
and `Agibuild.Fulora.Adapters.Android`, which require Xcode and the Android
workload respectively. On hosts without those, opening the `.slnx` in an IDE or
running `dotnet build Agibuild.Fulora.slnx` fails with cryptic SDK errors.

Use the platform-stripped solution filter instead — same projects minus the
four mobile-only ones:

```bash
dotnet build  Agibuild.Fulora.NoMobile.slnf
dotnet test   Agibuild.Fulora.NoMobile.slnf
# Or open Agibuild.Fulora.NoMobile.slnf directly in Visual Studio / Rider / VS Code C# Dev Kit.
```

`nuke Build` and `nuke BuildAll` do the same workload detection automatically,
so the slnf is only needed for raw `dotnet` and IDE entry points.

---

## Nuke Target Cheat Sheet

Run any of these with `./build.sh <Target>` (or `.\build.ps1 <Target>` on Windows):

| Target | Purpose | Typical use |
|---|---|---|
| `LocalPreflight` | Format + Build + UnitTests | Before every push |
| `Build`          | Platform-aware build (skips missing workloads) | Quick compile sanity check |
| `BuildAll`       | Full solution build via dynamic slnf | Pre-CodeQL, deep static analysis |
| `Format`         | `dotnet format --verify-no-changes` | Style gate only |
| `FastUnitTests`  | CLI unit tests only (~30 s) | Tight TDD loop |
| `UnitTests`      | All non-platform unit tests | Standard correctness check |
| `Coverage`       | UnitTests + cobertura + threshold gate | Mirrors a CI gate locally |
| `IntegrationTests` | Automation lane integration tests | Validating adapter wiring |
| `AutomationLaneReport` | All automation lanes + JSON report | What CI runs |
| `Ci`             | Full CI pipeline locally | Sanity-check before opening a release PR |
| `MutationTest`   | Stryker.NET on core profiles | Proving test strength of a refactor |
| `NugetPackageTest` | Pack → install → smoke test | Validating packaging changes |

Discover everything: `./build.sh --help`.

---

## Test Layout

```
tests/
├── Agibuild.Fulora.UnitTests/             # core unit tests, the bulk of coverage
├── Agibuild.Fulora.Cli.UnitTests/         # CLI surface tests
├── Agibuild.Fulora.Auth.OAuth.Tests/      # OAuth library tests
├── Agibuild.Fulora.Telemetry.*/           # provider tests
├── Agibuild.Fulora.Integration.Tests/     # in-process integration
├── Agibuild.Fulora.Integration.Tests.Automation/  # automation lane (mock + real)
└── Agibuild.Fulora.Integration.Tests.{Desktop,Browser,iOS,Android}/  # E2E per host
```

Coverage threshold (enforced by `nuke Coverage`):

* line ≥ **96 %**
* branch ≥ **93 %**

Mutation testing (`nuke MutationTest`) is scoped to **core business projects**
only — provider integration projects (`Agibuild.Fulora.AI.Ollama` /
`Agibuild.Fulora.AI.OpenAI`, etc.) are deliberately excluded. See
[`.cursor/rules/development-workflow.mdc`](.cursor/rules/development-workflow.mdc).

---

## Workflow Conventions

* **OpenSpec first.** Non-trivial changes start as a proposal under `openspec/`,
  then a spec, then implementation. Hotfixes and typo-level fixes are exempt.
  See `.cursor/rules/development-workflow.mdc` for the full rule.
* **Tests with every functional change.** No "I'll add tests later" PRs.
* **Commits in English; conversation/review can be Chinese.** Commit messages,
  comments, and code identifiers stay English; chat may be in either language.
* **Long-term value over short-term speed.** Choose the right architecture even
  if the diff is large. Avoid defensive `if (logger == null)` style fallbacks
  and minimal-touch patches that mask design issues.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `xcrun : error : SDK "iphoneos" cannot be located` | Use `Agibuild.Fulora.NoMobile.slnf` or `nuke Build`. |
| `error : Could not load file or assembly 'System.Runtime, Version=...` during `dotnet format` | Run `nuke Restore` first; `dotnet format` needs a current restore graph. |
| `nuke LocalPreflight` reports format errors after a generated-code change | Run `dotnet format Agibuild.Fulora.NoMobile.slnf`, re-stage, re-run. |
| `nuke Coverage` fails on threshold | Inspect `artifacts/coverage-report/index.html`, then add tests for the highlighted untested branches. |
| Stryker hangs forever on a profile | Filter with `nuke MutationTest --mutation-profile core` to bisect which profile is misbehaving. |
| `dotnet test` lock-file errors after a `version.json` bump | Delete the offending `packages.lock.json` and `nuke Restore`. |

---

## Where to Look for What

| Topic | Path |
|---|---|
| Bridge / typed JS<->C# RPC | `docs/articles/bridge-guide.md` |
| SPA hosting & dev server | `docs/articles/spa-hosting.md` |
| End-to-end architecture | `docs/articles/architecture.md` |
| Platform status & roadmap | `docs/platform-status.md`, `docs/product-platform-roadmap.md` |
| Build/CI definitions | `build/Build*.cs` |
| Coding standards & DI rules | `.cursor/rules/`, `.cursor/skills/` |
