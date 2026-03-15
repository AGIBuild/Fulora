## ADDED Requirements

### Requirement: FuloraException base type
The framework SHALL define a `FuloraException` base exception type with a structured `ErrorCode` property. All framework-specific exceptions MUST derive from FuloraException.

#### Scenario: Exception includes error code
- **WHEN** a FuloraException is thrown
- **THEN** it MUST have a non-null ErrorCode that maps to the bridge error taxonomy

### Requirement: No empty catch blocks
Runtime, AI, and Bridge code SHALL NOT contain empty catch blocks. Every catch block MUST either log the exception, rethrow, or translate to a FuloraException.

#### Scenario: Silent catch elimination
- **WHEN** an exception occurs in a catch block
- **THEN** it MUST be logged at minimum Warning level or rethrown

### Requirement: Bridge error code alignment
C# exceptions crossing the bridge boundary SHALL be mapped to structured JSON-RPC error codes consistent with the bridge error taxonomy (code/message/data).

#### Scenario: Runtime exception reaches JS
- **WHEN** a C# method exposed via bridge throws a FuloraException
- **THEN** the JS caller MUST receive a structured error with matching error code
