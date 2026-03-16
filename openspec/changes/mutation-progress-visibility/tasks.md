## 1. Mutation Orchestration Progress Model

- [x] 1.1 Add `MutationProfile` parameter to Nuke mutation target with strict allowed-value validation (`core/runtime/ai`) and default all-profile mode.
- [x] 1.2 Add profile-level progress logging and step-summary emission (start/end/elapsed/report path) for CI visibility.

## 2. Workflow Progress Visibility

- [x] 2.1 Refactor `mutation-testing.yml` into profile-level matrix execution wired to `--mutation-profile`.
- [x] 2.2 Publish per-profile mutation artifacts with deterministic naming for triage.

## 3. Governance and Verification

- [x] 3.1 Extend mutation governance tests to require profile-level workflow decomposition and explicit profile argument wiring.
- [x] 3.2 Run governance tests and mutation-profile smoke command to verify progress-visible execution path.
