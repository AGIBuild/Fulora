## ADDED Requirements

### Requirement: Bridge generator SHALL produce a canonical contract IR
The bridge generation pipeline SHALL normalize discovered bridge contracts into a canonical intermediate representation (IR) that includes service identity, method signatures, parameter metadata, DTO graph dependencies, event contracts, and error metadata required by downstream emitters.

#### Scenario: IR captures complete contract graph
- **WHEN** one or more `[JsExport]` or `[JsImport]` interfaces reference nested DTO types
- **THEN** the generator SHALL produce an IR that includes all reachable DTO type nodes and edges needed for deterministic artifact emission

#### Scenario: IR normalizes naming deterministically
- **WHEN** source interfaces use C# naming conventions (`PascalCase`, optional custom service names)
- **THEN** the IR SHALL carry deterministic canonical names for service/method/parameter identity used by all emitters

### Requirement: All bridge artifacts SHALL be emitted from the same IR
Any bridge output artifact (declarations, typed clients, mocks, host manifests) SHALL be generated from the canonical IR and SHALL NOT re-discover contract shape independently.

#### Scenario: Multi-artifact outputs stay semantically aligned
- **WHEN** a bridge contract changes (for example method rename or DTO field addition)
- **THEN** all generated artifacts SHALL reflect the change in the same build without semantic drift between artifact types

#### Scenario: Deterministic output stability
- **WHEN** the same source inputs are built repeatedly
- **THEN** generated artifacts SHALL be byte-stable except for explicitly versioned metadata fields
