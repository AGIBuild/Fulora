## ADDED Requirements

### Requirement: Mutation workflow SHALL avoid opaque long-running single-step execution
Build pipeline resilience governance MUST ensure mutation CI does not collapse all mutation profiles into a single opaque execution step that prevents profile-level progress diagnosis.

#### Scenario: Governance rejects opaque mutation orchestration
- **WHEN** mutation workflow defines one mutation execution step without profile-level decomposition
- **THEN** governance checks fail with actionable diagnostics requiring profile-level progress visibility

### Requirement: Mutation governance SHALL enforce profile argument wiring
Mutation workflow governance MUST verify that each profile execution path invokes Nuke mutation orchestration with an explicit profile selector argument.

#### Scenario: Workflow omits explicit profile selector
- **WHEN** a mutation workflow mutation step calls Nuke without `--mutation-profile`
- **THEN** governance fails and reports missing profile argument wiring
