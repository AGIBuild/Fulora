## 1. Infrastructure evolution (Build.Governance.Infrastructure.cs, Build.ProcessHelpers.cs)

- [x] 1.1 Create `Build.Governance.Infrastructure.cs` with `GovernanceFailure` record and `RunGovernanceCheck`/`RunGovernanceCheckAsync` static helpers
- [x] 1.2 Evolve `GovernanceCheckResult.Failures` from `IReadOnlyList<string>` to `IReadOnlyList<GovernanceFailure>`
- [x] 1.3 Update `RunGovernanceCheck` assertion message formatting: `[{InvariantId}] {SourceArtifact}: expected {Expected}, actual {Actual}`
- [x] 1.4 Add `GovernanceCamelCaseJsonOptions` (`WriteIndented = true`, `PropertyNamingPolicy = CamelCase`) in `Build.ProcessHelpers.cs`
- [x] 1.5 Add `WriteGovernanceReport` method (or update `WriteJsonReport` to accept `JsonSerializerOptions` override) to use camelCase options for governance reports

## 2. File decomposition (completed)

- [x] 2.1 Extract `DependencyVulnerabilityGovernance` to `Build.Governance.Dependency.cs`
- [x] 2.2 Extract `TypeScriptDeclarationGovernance` to `Build.Governance.TypeScript.cs`
- [x] 2.3 Extract `SampleTemplatePackageReferenceGovernance` to `Build.Governance.Sample.cs`
- [x] 2.4 Extract `RuntimeCriticalPathExecutionGovernance` to `Build.Governance.RuntimePath.cs`
- [x] 2.5 Extract `OpenSpecStrictGovernance` to `Build.Governance.OpenSpec.cs`
- [x] 2.6 Extract `BridgeDistributionGovernance`, `DistributionReadinessGovernance`, `AdoptionReadinessGovernance` to `Build.Governance.Distribution.cs`
- [x] 2.7 Extract `ReleaseCloseoutSnapshot`, `ContinuousTransitionGateGovernance`, `ReleaseOrchestrationGovernance` to `Build.Governance.Release.cs`
- [x] 2.8 `SolutionConsistencyGovernance` exists in `Build.Governance.Solution.cs`
- [x] 2.9 Original `Build.Governance.cs` deleted

## 3. Target migration to GovernanceFailure

### 3a. Tier 1 — targets already using RunGovernanceCheck (migrate from string failures to GovernanceFailure)

- [x] 3.1 Migrate `DependencyVulnerabilityGovernance` — construct GovernanceFailure with Category="dependency-vulnerability", InvariantId from scan context
- [x] 3.2 Migrate `TypeScriptDeclarationGovernance` — construct GovernanceFailure with Category="typescript-declaration"
- [x] 3.3 Migrate `SampleTemplatePackageReferenceGovernance` — construct GovernanceFailure with Category="sample-template-package"
- [x] 3.4 Migrate `SolutionConsistencyGovernance` — construct GovernanceFailure with Category="solution-consistency"
- [x] 3.5 Migrate `BridgeDistributionGovernance` — construct GovernanceFailure with Category="bridge-distribution"

### 3b. Tier 2 — targets requiring adoption of RunGovernanceCheck + GovernanceFailure

- [x] 3.6 Migrate `RuntimeCriticalPathExecutionGovernance` to use RunGovernanceCheck (currently uses direct WriteJsonReport + Assert.Fail)
- [x] 3.7 Migrate `ContinuousTransitionGateGovernance` to use RunGovernanceCheck; map `TransitionGateDiagnosticEntry` → GovernanceFailure (Group→Category, ArtifactPath→SourceArtifact, Lane encoded in Expected/Actual)

### 3c. Excluded from migration (no changes needed)

- [x] ~~3.8 WarningGovernance~~ — excluded: specialized classification model (known-baseline/actionable/new-regression), not a standard governance check pattern
- [x] ~~3.9 AutomationLaneReport~~ — excluded: CI evidence collector, not a governance check
- [x] ~~3.10 OpenSpecStrictGovernance~~ — excluded: outputs plain text log via npm exec, not structured JSON report
- [x] ~~3.11 ReleaseCloseoutSnapshot~~ — excluded: complex evidence composite, not a check-and-report target
- [x] ~~3.12 ReleaseOrchestrationGovernance~~ — excluded: orchestration aggregator that reads other reports; already uses GovernanceFailure internally
- [x] ~~3.13 DistributionReadinessGovernance~~ — excluded: already uses GovernanceFailure directly, custom schema with summary/provenance
- [x] ~~3.14 AdoptionReadinessGovernance~~ — excluded: already uses GovernanceFailure directly, custom schema with blocking/advisory tiers

## 4. Ci target simplification

- [x] 4.1 Analyze transitive dependency graph of Ci target via `nuke Ci --plan`
- [x] 4.2 Remove redundant direct dependencies from `Ci.DependsOn` that are already transitively required through `ReleaseOrchestrationGovernance`
- [x] 4.3 Verify `nuke Ci --plan` shows identical execution order after simplification

## 5. Verification

- [ ] 5.1 Before/after report JSON comparison: generate governance reports before migration, then after, and diff for field-level compatibility (all camelCase field names, failureCount values, invariant IDs)
- [x] 5.2 Downstream consumer contract test: verify ReleaseOrchestrationGovernance can still parse all upstream reports after migration (distribution-readiness, adoption-readiness, transition-gate, closeout-snapshot)
- [x] 5.3 Unit test updates: update `AutomationLaneGovernanceTests.cs` file path references from `"build/Build.Governance.cs"` to current file paths
- [ ] 5.4 Build compilation: `nuke Ci --configuration Release` passes with zero new warnings
- [ ] 5.5 CI dependency graph validation: `nuke Ci --plan` output matches expected target execution order
