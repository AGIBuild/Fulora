## MODIFIED Requirements

### Requirement: Structural refactors preserve public API
Structural refactors of Runtime classes SHALL NOT change any public or protected API surface. All existing public types, methods, properties, and events MUST remain accessible with identical signatures and semantics. Internal type reorganization (moving inner types to separate files, extracting coordinators) is permitted. Test assertions MUST NOT require changes.

#### Scenario: Public API compatibility after decomposition
- **WHEN** WebViewCore, RuntimeBridgeService, or WebViewShellExperience are decomposed
- **THEN** all public/protected members MUST have identical signatures and all existing tests MUST pass without assertion changes

#### Scenario: Internal type visibility
- **WHEN** inner types are extracted to separate files
- **THEN** they MUST remain internal to the Runtime assembly via internal access modifier or InternalsVisibleTo
