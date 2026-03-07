## ADDED Requirements

### Requirement: Content filter pipeline
The system SHALL provide an `IAiContentFilter` interface with `FilterInputAsync` and `FilterOutputAsync` methods, composable as an ordered pipeline via DI.

#### Scenario: Multiple filters execute in order
- **WHEN** filters A and B are registered in order
- **THEN** input filtering runs A then B, and output filtering runs A then B

#### Scenario: No filters registered
- **WHEN** no `IAiContentFilter` implementations are registered
- **THEN** the content gate middleware passes input and output through unchanged

### Requirement: Input filtering
The system SHALL pass user prompts through the content filter pipeline before sending to the AI provider.

#### Scenario: Filter blocks input
- **WHEN** a filter returns `ContentFilterResult.Block(reason)` for an input prompt
- **THEN** the system throws `AiContentBlockedException` with the reason and does NOT send the prompt to the provider

#### Scenario: Filter transforms input
- **WHEN** a filter returns `ContentFilterResult.Transform(modifiedContent)` for an input prompt
- **THEN** the system uses the modified content for the AI call

#### Scenario: Filter allows input
- **WHEN** a filter returns `ContentFilterResult.Allow` for an input prompt
- **THEN** the system passes the input to the next filter or to the provider

### Requirement: Output filtering
The system SHALL pass AI provider responses through the content filter pipeline before returning to the caller.

#### Scenario: Filter blocks output
- **WHEN** a filter returns `ContentFilterResult.Block(reason)` for an AI response
- **THEN** the system throws `AiContentBlockedException` with the reason and does NOT return the response to the caller

#### Scenario: Filter redacts output
- **WHEN** a filter returns `ContentFilterResult.Transform(redactedContent)` for an AI response
- **THEN** the system returns the redacted content to the caller

### Requirement: Streaming output filtering
The system SHALL support filtering of streaming AI responses on a per-chunk basis.

#### Scenario: Filter blocks a streaming chunk
- **WHEN** a filter blocks a streaming chunk
- **THEN** the chunk is omitted from the stream and a warning is logged

#### Scenario: Filter transforms a streaming chunk
- **WHEN** a filter transforms a streaming chunk
- **THEN** the modified chunk is yielded in the stream
