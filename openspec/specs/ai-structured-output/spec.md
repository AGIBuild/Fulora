## Purpose

Specifies JSON Schema generation from C# types via source generator and typed completion APIs (`CompleteAsync<T>`, `CompleteStreamingAsync<T>`) for structured AI outputs.

## Requirements

### Requirement: JSON Schema generation from C# types
The source generator SHALL emit a JSON Schema string constant for C# types used as AI structured output targets.

#### Scenario: Flat record type generates valid schema
- **WHEN** a record `public record OrderSummary(string Title, int Count, decimal Total)` is used as a structured output target
- **THEN** the generator emits a JSON Schema with `type: "object"`, properties for each field with correct types, and `required` array

#### Scenario: Enum property generates string enum schema
- **WHEN** a record contains an `OrderStatus` enum property
- **THEN** the JSON Schema property has `type: "string"` with `enum` array containing all enum member names

#### Scenario: Nullable property is not required
- **WHEN** a record contains a `string? Notes` property
- **THEN** the JSON Schema does not include "Notes" in the `required` array

#### Scenario: Collection property generates array schema
- **WHEN** a record contains a `List<string> Tags` property
- **THEN** the JSON Schema property has `type: "array"` with `items: { type: "string" }`

### Requirement: Typed completion API
The system SHALL provide `CompleteAsync<T>(prompt)` and `CompleteStreamingAsync<T>(prompt)` extension methods on `IChatClient` that return deserialized, validated results.

#### Scenario: Successful structured completion
- **WHEN** developer calls `await ai.CompleteAsync<OrderSummary>(prompt)`
- **THEN** the system sends the prompt with JSON Schema constraint, deserializes the response to `OrderSummary`, and returns it

#### Scenario: Response fails validation triggers retry
- **WHEN** the LLM response does not match the JSON Schema (e.g., missing required field)
- **THEN** the system retries with the original prompt plus the validation error message, up to the configured max attempts (default 3)

#### Scenario: All retries exhausted
- **WHEN** all retry attempts produce invalid structured output
- **THEN** the system throws `AiStructuredOutputException` containing the last validation error and the raw LLM response

### Requirement: Bridge exposure of structured output
The system SHALL expose structured output to JS, with TypeScript types matching the C# types.

#### Scenario: JS receives typed structured output
- **WHEN** JS calls `aiChat.completeTyped<OrderSummary>(prompt)` via bridge
- **THEN** the response is a TypeScript object matching the `OrderSummary` type definition
