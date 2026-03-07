## ADDED Requirements

### Requirement: Per-call token tracking
The system SHALL extract and record token usage (prompt tokens, completion tokens, total tokens) from every AI provider response.

#### Scenario: Non-streaming call records usage
- **WHEN** an `IChatClient.CompleteAsync` call returns a response with `Usage` populated
- **THEN** the metering middleware records prompt_tokens, completion_tokens, total_tokens, model name, and provider name

#### Scenario: Streaming call records usage at completion
- **WHEN** a streaming call completes and the final chunk contains `Usage`
- **THEN** the metering middleware records the cumulative token usage

#### Scenario: Response without usage data
- **WHEN** an AI provider response has null `Usage`
- **THEN** the metering middleware records the call with tokens marked as unknown (not zero)

### Requirement: Cost estimation
The system SHALL estimate cost based on configurable per-model token pricing.

#### Scenario: Cost calculated from pricing table
- **WHEN** a model is registered with pricing `{ promptPer1k: 0.01, completionPer1k: 0.03 }` and a call uses 1000 prompt + 500 completion tokens
- **THEN** the estimated cost is $0.01 + $0.015 = $0.025

#### Scenario: Unknown model pricing
- **WHEN** a call uses a model not in the pricing table
- **THEN** the estimated cost is null (not zero), and a warning is logged

### Requirement: Budget enforcement
The system SHALL enforce configurable token/cost budgets and reject calls when budget is exceeded.

#### Scenario: Single-call token limit
- **WHEN** a call specifies max_tokens exceeding the configured single-call limit
- **THEN** the system rejects the call with `AiBudgetExceededException` before sending to provider

#### Scenario: Cumulative budget exceeded
- **WHEN** the cumulative token usage exceeds the configured period budget (daily/hourly)
- **THEN** subsequent calls are rejected with `AiBudgetExceededException`

### Requirement: Telemetry export
The system SHALL emit token usage metrics via `System.Diagnostics.Metrics` and `IBridgeTracer`.

#### Scenario: OpenTelemetry metrics emitted
- **WHEN** an AI call completes with token usage
- **THEN** metrics `fulora.ai.tokens.prompt`, `fulora.ai.tokens.completion`, and `fulora.ai.cost.estimated` are emitted with model and provider tags

#### Scenario: Bridge tracer integration
- **WHEN** an AI call completes
- **THEN** the existing `IBridgeTracer` receives a trace entry with AI-specific metadata (model, tokens, cost, latency)
