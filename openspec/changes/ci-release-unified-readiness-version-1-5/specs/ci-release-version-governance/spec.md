## ADDED Requirements

### Requirement: CI and release SHALL share one readiness contract
The build orchestration SHALL define a single machine-checkable readiness contract used by both CI validation and release promotion paths. Release SHALL NOT introduce additional quality gates that are absent from CI readiness.

#### Scenario: Shared readiness contract is enforced
- **WHEN** CI and release lane dependencies are evaluated
- **THEN** both lanes reference the same readiness invariant set
- **AND** release-specific checks are limited to publish authorization and artifact provenance verification

### Requirement: Repository version baseline SHALL be fixed at 1.5
The repository-level shared version source SHALL define the baseline major and minor as `1.5` for all packable projects participating in release automation.

#### Scenario: Baseline version is centralized
- **WHEN** version-related repository configuration is evaluated
- **THEN** the baseline major/minor is resolved from a single shared source
- **AND** all NuGet and npm package version computations inherit baseline `1.5`

### Requirement: CI artifact version SHALL follow X.Y.Z.<run_number>
CI version computation SHALL produce artifact/package versions in `X.Y.Z.<run_number>` format and SHALL NOT append textual prerelease identifiers such as `ci`.

#### Scenario: CI version format is compliant
- **WHEN** a CI run computes package version for artifacts
- **THEN** the computed version matches numeric four-part format `X.Y.Z.<run_number>`
- **AND** no `-ci`, `.ci`, or equivalent text suffix is present

### Requirement: Release SHALL publish CI-produced artifacts without rebuild
Release promotion SHALL consume immutable artifacts and version manifest produced by CI and SHALL NOT rebuild packages before publishing.

#### Scenario: Release promotes CI artifacts directly
- **WHEN** release is triggered for a commit with successful CI readiness
- **THEN** release downloads CI artifact bundle and provenance manifest
- **AND** release publishes those exact artifacts after manifest/version parity validation

### Requirement: Tag workflow SHALL NOT act as release version authority
Tag automation (including `create-tag.yml`) SHALL NOT be the authority that computes publishable package versions once this governance model is enabled.

#### Scenario: Version authority remains in CI manifest
- **WHEN** a release publish action starts
- **THEN** publish version is sourced from CI-generated manifest and shared baseline logic
- **AND** tag metadata, if present, is treated as traceability metadata rather than version computation input
