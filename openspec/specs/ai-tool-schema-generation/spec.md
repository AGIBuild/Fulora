## ADDED Requirements

### Requirement: AiTool attribute
The system SHALL provide an `[AiTool]` attribute applicable to `[JsExport]` interfaces and individual methods, signaling the source generator to emit AI function tool schemas.

#### Scenario: Attribute on interface
- **WHEN** `[AiTool]` is applied to a `[JsExport]` interface
- **THEN** the source generator emits tool schemas for ALL public methods on that interface

#### Scenario: Attribute on individual method
- **WHEN** `[AiTool]` is applied to a specific method on a `[JsExport]` interface
- **THEN** the source generator emits a tool schema for only that method

#### Scenario: AiTool without JsExport
- **WHEN** `[AiTool]` is applied to an interface without `[JsExport]`
- **THEN** the source generator emits diagnostic `AGBR009` warning that `[AiTool]` requires `[JsExport]`

### Requirement: OpenAI-compatible tool schema generation
The source generator SHALL emit tool schemas in OpenAI function-calling format (JSON).

#### Scenario: Method with parameters generates correct schema
- **WHEN** method `Task<Order[]> SearchAsync(string query, int limit = 10)` has `[AiTool]`
- **THEN** generated schema has `name: "search"`, `description` from XML doc, `parameters` with `query` (required, string) and `limit` (optional, integer, default 10)

#### Scenario: XML doc comment becomes description
- **WHEN** a method has `/// <summary>Search orders by keyword</summary>`
- **THEN** the tool schema `description` field contains "Search orders by keyword"

#### Scenario: No XML doc generates diagnostic
- **WHEN** a `[AiTool]` method has no XML doc summary
- **THEN** the source generator emits diagnostic `AGBR010` info suggesting to add a description

### Requirement: Tool schema registry
The system SHALL provide an `IAiToolRegistry` that collects all generated tool schemas at startup and exposes them for AI function calling.

#### Scenario: Auto-discovery at startup
- **WHEN** the DI container is built with `AddFuloraAi()`
- **THEN** `IAiToolRegistry` contains tool schemas for all `[AiTool]`-marked methods discovered via source-generated registrations

#### Scenario: Tool invocation
- **WHEN** an AI provider returns a function call for tool "search" with arguments `{"query": "test", "limit": 5}`
- **THEN** `IAiToolRegistry.InvokeAsync("search", args)` deserializes and calls the underlying `[JsExport]` service method
