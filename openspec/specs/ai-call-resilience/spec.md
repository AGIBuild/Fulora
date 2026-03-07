## ADDED Requirements

### Requirement: Retry with exponential backoff
The system SHALL retry failed AI provider calls with configurable exponential backoff.

#### Scenario: Transient failure triggers retry
- **WHEN** an `IChatClient.CompleteAsync` call throws `HttpRequestException` (transient)
- **THEN** the system retries up to the configured max attempts (default 3) with exponential backoff (default base 2s)

#### Scenario: Non-transient failure does not retry
- **WHEN** an `IChatClient.CompleteAsync` call throws `AuthenticationException`
- **THEN** the system propagates the exception immediately without retry

### Requirement: Call timeout
The system SHALL enforce a configurable timeout on AI provider calls.

#### Scenario: Call exceeds timeout
- **WHEN** an AI provider call does not complete within the configured timeout (default 30s)
- **THEN** the system cancels the call and throws `TimeoutRejectedException`

#### Scenario: Streaming timeout applies to first chunk
- **WHEN** a streaming call does not produce the first chunk within the timeout
- **THEN** the system cancels the stream and throws `TimeoutRejectedException`

### Requirement: Circuit breaker
The system SHALL open a circuit breaker after consecutive failures, preventing further calls for a recovery period.

#### Scenario: Circuit opens after consecutive failures
- **WHEN** 5 consecutive AI provider calls fail (configurable)
- **THEN** the circuit breaker opens and subsequent calls throw `BrokenCircuitException` for the configured break duration (default 60s)

#### Scenario: Circuit half-opens after break duration
- **WHEN** the break duration expires
- **THEN** the circuit allows one trial call; if it succeeds, the circuit closes

### Requirement: Rate limiter
The system SHALL enforce configurable rate limits on AI provider calls.

#### Scenario: Rate limit exceeded
- **WHEN** AI provider calls exceed the configured rate (default: no limit)
- **THEN** the system rejects the call with `RateLimiterRejectedException`

### Requirement: DI configuration
The system SHALL allow resilience options to be configured via the `AddFuloraAi` builder.

#### Scenario: Custom resilience configuration
- **WHEN** developer calls `ai.AddResilience(r => r.MaxRetries(5).Timeout(TimeSpan.FromSeconds(60)))`
- **THEN** the resilience middleware uses the specified retry count and timeout
