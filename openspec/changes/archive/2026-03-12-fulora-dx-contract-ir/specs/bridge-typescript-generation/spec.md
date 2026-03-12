## MODIFIED Requirements

### Requirement: TypeScript emitter maps supported CLR types deterministically
The generator SHALL map supported CLR types to TypeScript declarations using the canonical bridge contract IR and SHALL emit deterministic service/type naming across all TypeScript artifacts. The type mapper SHALL correctly handle nested generic types by using bracket-depth-aware parsing instead of simple comma splitting. The binary CLR type `byte[]` SHALL map to `Uint8Array`.

In addition to declaration output, generation SHALL produce typed client and mock artifacts from the same IR so that DTO and method contracts remain consistent across declaration, invocation, and mock surfaces.

#### Scenario: CLR-to-TypeScript mapping is deterministic
- **WHEN** bridge interfaces include primitive and common structured CLR types
- **THEN** generated declarations use stable TypeScript mappings and service signatures

#### Scenario: byte array mapping produces typed binary declaration
- **WHEN** a bridge method uses `byte[]` as parameter or return type
- **THEN** generated TypeScript declaration uses `Uint8Array` for that position

#### Scenario: Typed client output remains aligned with declarations
- **WHEN** a bridge service method or DTO shape changes in C# contracts
- **THEN** generated typed client output SHALL reflect the same updated method and DTO contract in the same generation run

#### Scenario: Mock artifact remains aligned with declarations
- **WHEN** bridge contract generation emits updated service methods
- **THEN** generated mock artifact SHALL expose the same method identity and payload shape expectations as declaration output
