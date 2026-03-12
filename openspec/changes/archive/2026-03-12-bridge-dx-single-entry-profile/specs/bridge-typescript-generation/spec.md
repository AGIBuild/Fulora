## MODIFIED Requirements

### Requirement: TypeScript emitter maps supported CLR types deterministically
The generator SHALL map supported CLR types to TypeScript declarations and SHALL emit per-service interfaces with deterministic naming and JSDoc metadata. The type mapper SHALL correctly handle nested generic types by using bracket-depth-aware parsing instead of simple comma splitting. The binary CLR type `byte[]` SHALL map to `Uint8Array`.

In addition to declaration output, generation SHALL produce typed client and mock artifacts from the same semantic model, and official maintained sample/template app layers SHALL treat generated artifacts (`bridge.d.ts`, `bridge.client.ts`, `bridge.mock.ts`) as the normative contract-consumption surface.

#### Scenario: CLR-to-TypeScript mapping is deterministic
- **WHEN** bridge interfaces include primitive and common structured CLR types
- **THEN** generated declarations use stable TypeScript mappings and service signatures

#### Scenario: byte array mapping produces typed binary declaration
- **WHEN** a bridge method uses `byte[]` as parameter or return type
- **THEN** generated TypeScript declaration uses `Uint8Array` for that position

#### Scenario: Generated artifacts remain semantically aligned in one build
- **WHEN** a bridge service method or DTO contract changes
- **THEN** declaration, client, and mock artifacts SHALL reflect the same updated contract in the same generation run

#### Scenario: Official template/sample app layer consumes generated artifacts by default
- **WHEN** official maintained template or sample web bridge source is inspected
- **THEN** service proxies and DTO contracts SHALL be consumed from generated artifacts rather than handwritten bridge contract duplication
