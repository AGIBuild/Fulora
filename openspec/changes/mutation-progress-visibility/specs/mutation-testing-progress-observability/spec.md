## ADDED Requirements

### Requirement: Mutation workflow SHALL expose profile-level execution progress
The mutation pipeline MUST expose independent progress for `core`, `runtime`, and `ai` mutation profiles so maintainers can identify active/stalled profiles without reading a monolithic long-running step.

#### Scenario: Profile jobs are visible in workflow UI
- **WHEN** `Mutation Testing` workflow is triggered
- **THEN** GitHub Actions displays separate execution units for each mutation profile with independent status and timing

### Requirement: Nuke mutation orchestration SHALL support profile-scoped execution
`MutationTest` orchestration MUST accept an explicit profile selector and MUST execute only the requested profile when provided; without selector it SHALL execute all configured profiles.

#### Scenario: Targeted profile execution
- **WHEN** build command is invoked with `--mutation-profile runtime`
- **THEN** mutation execution runs only the `runtime` profile and writes report output under the `runtime` directory

### Requirement: Mutation runs SHALL publish deterministic progress summaries
Mutation execution MUST emit machine-readable start/end/elapsed metadata per profile to step summary output when running in GitHub Actions.

#### Scenario: Step summary includes profile timing data
- **WHEN** mutation profile execution completes in CI
- **THEN** step summary contains profile name, start timestamp, end timestamp, elapsed duration, and report path
