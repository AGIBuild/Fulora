## ADDED Requirements

### Requirement: Solution includes all built projects
Every project that is built and packaged by the Nuke build system SHALL be included in the main Agibuild.Fulora.sln solution file.

#### Scenario: Missing project detection
- **WHEN** a project is listed in Nuke build targets but not in the .sln
- **THEN** the solution-consistency governance target MUST report a failure

### Requirement: Solution consistency governance target
The build system SHALL include a governance target that validates .sln ↔ Nuke build scope consistency and reports violations.

#### Scenario: Governance check passes
- **WHEN** all Nuke-built projects are present in the .sln
- **THEN** the governance target MUST pass with zero failures
