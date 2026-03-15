## 1. Infrastructure extraction

- [ ] 1.1 Create `Build.Governance.Infrastructure.cs` with `GovernanceFailure` record, `GovernanceReportPayload<T>` record, and `RunGovernanceCheck` static helper
- [ ] 1.2 Add `WriteIndentedCamelCaseJsonOptions` for report serialization consistency
- [ ] 1.3 Verify GovernanceRunner produces identical JSON field semantics as current anonymous objects

## 2. File decomposition

- [ ] 2.1 Extract `DependencyVulnerabilityGovernance` to `Build.Governance.Dependency.cs`
- [ ] 2.2 Extract `TypeScriptDeclarationGovernance` to `Build.Governance.TypeScript.cs`
- [ ] 2.3 Extract `SampleTemplatePackageReferenceGovernance` to `Build.Governance.Sample.cs`
- [ ] 2.4 Extract `RuntimeCriticalPathExecutionGovernance` to `Build.Governance.RuntimePath.cs`
- [ ] 2.5 Extract `OpenSpecStrictGovernance` to `Build.Governance.OpenSpec.cs`
- [ ] 2.6 Extract `BridgeDistributionGovernance`, `DistributionReadinessGovernance`, `AdoptionReadinessGovernance` to `Build.Governance.Distribution.cs`
- [ ] 2.7 Extract `ReleaseCloseoutSnapshot`, `ContinuousTransitionGateGovernance`, `ReleaseOrchestrationGovernance` to `Build.Governance.Release.cs`
- [ ] 2.8 Delete the original `Build.Governance.cs` after all targets are migrated

## 3. Target migration to GovernanceRunner

- [ ] 3.1 Migrate DependencyVulnerabilityGovernance to use GovernanceRunner + GovernanceFailure
- [ ] 3.2 Migrate TypeScriptDeclarationGovernance
- [ ] 3.3 Migrate SampleTemplatePackageReferenceGovernance
- [ ] 3.4 Migrate RuntimeCriticalPathExecutionGovernance
- [ ] 3.5 Migrate OpenSpecStrictGovernance
- [ ] 3.6 Migrate BridgeDistributionGovernance
- [ ] 3.7 Migrate DistributionReadinessGovernance
- [ ] 3.8 Migrate AdoptionReadinessGovernance
- [ ] 3.9 Migrate ReleaseCloseoutSnapshot
- [ ] 3.10 Migrate ContinuousTransitionGateGovernance
- [ ] 3.11 Migrate ReleaseOrchestrationGovernance
- [ ] 3.12 Migrate WarningGovernance (Build.WarningGovernance.cs) to use GovernanceFailure
- [ ] 3.13 Migrate AutomationLaneReport (Build.Testing.cs) to use GovernanceRunner where applicable

## 4. Ci target simplification

- [ ] 4.1 Analyze transitive dependency graph of Ci target
- [ ] 4.2 Remove redundant direct dependencies from Ci.DependsOn that are already transitively required
- [ ] 4.3 Verify `nuke Ci --plan` shows identical execution order

## 5. Verification

- [ ] 5.1 Run `nuke Ci --configuration Release` locally and verify all governance targets pass
- [ ] 5.2 Compare governance report JSON outputs before and after migration for field-level compatibility
- [ ] 5.3 Verify build compiles with zero warnings
