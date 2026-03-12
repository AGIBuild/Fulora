## Purpose

Define requirements for the JavaScript-side middleware pipeline in the BridgeClient.
## Requirements
### Requirement: BridgeClient supports middleware registration via use()

The `BridgeClient` interface SHALL expose a `use(middleware)` method that registers a middleware function into the client's execution pipeline.

#### Scenario: Registered middleware executes on bridge call

- **WHEN** a middleware is registered via `bridgeClient.use(myMiddleware)` before a service call
- **THEN** `myMiddleware` SHALL be invoked with `BridgeCallContext` and `next` function for every subsequent bridge call

#### Scenario: Multiple middlewares execute in registration order (onion model)

- **WHEN** `bridgeClient.use(A)` then `bridgeClient.use(B)` are called
- **AND** a bridge call is made
- **THEN** middleware A's pre-logic executes first, then B's pre-logic, then the RPC call, then B's post-logic, then A's post-logic

#### Scenario: Client without middleware works identically to current behavior

- **WHEN** no middleware is registered via `use()`
- **THEN** bridge calls SHALL behave identically to the current middleware-free implementation

### Requirement: BridgeCallContext provides typed call metadata

The middleware pipeline SHALL provide a `BridgeCallContext` object with typed metadata for each bridge call.

#### Scenario: Context includes service and method names

- **WHEN** a middleware receives a call context for `AppService.getCurrentUser`
- **THEN** `context.serviceName` SHALL be `"AppService"` and `context.methodName` SHALL be `"getCurrentUser"`

#### Scenario: Context includes start timestamp

- **WHEN** a middleware receives a call context
- **THEN** `context.startedAt` SHALL be a numeric timestamp (milliseconds since epoch) captured before middleware execution

#### Scenario: Context properties bag is mutable and shared across middleware

- **WHEN** middleware A sets `context.properties.set("correlationId", "abc")`
- **THEN** middleware B SHALL be able to read `context.properties.get("correlationId")` returning `"abc"`

### Requirement: Middleware can short-circuit the pipeline

A middleware SHALL be able to return a value or throw an error without calling `next()`, effectively short-circuiting the pipeline.

#### Scenario: Middleware that throws prevents RPC call

- **WHEN** a middleware throws an error without calling `next()`
- **THEN** the RPC call SHALL NOT be executed and the error SHALL propagate to the caller

#### Scenario: Middleware that returns without calling next prevents RPC call

- **WHEN** a middleware returns a cached value without calling `next()`
- **THEN** the RPC call SHALL NOT be executed and the cached value SHALL be returned to the caller

### Requirement: withLogging middleware logs bridge call details

The package SHALL provide a `withLogging(options?)` built-in middleware that logs bridge call information.

#### Scenario: Logging middleware logs service, method, and latency on success

- **WHEN** `withLogging()` is registered and a bridge call succeeds
- **THEN** the middleware SHALL log the service name, method name, and elapsed milliseconds

#### Scenario: Logging middleware logs error details on failure

- **WHEN** `withLogging()` is registered and a bridge call fails
- **THEN** the middleware SHALL log the service name, method name, and error message

### Requirement: withTimeout middleware rejects calls exceeding duration

The package SHALL provide a `withTimeout(ms)` built-in middleware that rejects bridge calls that exceed the specified duration.

#### Scenario: Call completing within timeout succeeds normally

- **WHEN** `withTimeout(5000)` is registered and a bridge call completes in 100ms
- **THEN** the call SHALL resolve with the normal result

#### Scenario: Call exceeding timeout rejects with TimeoutError

- **WHEN** `withTimeout(5000)` is registered and a bridge call takes longer than 5000ms
- **THEN** the call SHALL reject with a `BridgeTimeoutError` containing the timeout duration

### Requirement: withRetry middleware retries failed calls

The package SHALL provide a `withRetry(options)` built-in middleware that retries bridge calls on failure.

#### Scenario: Failed call is retried up to maxRetries

- **WHEN** `withRetry({ maxRetries: 3, delay: 100 })` is registered and a bridge call fails
- **THEN** the call SHALL be retried up to 3 times with 100ms delay between attempts

#### Scenario: Successful retry returns result

- **WHEN** a bridge call fails on the first attempt but succeeds on the second retry
- **THEN** the caller SHALL receive the successful result without error

#### Scenario: All retries exhausted throws last error

- **WHEN** a bridge call fails on all retry attempts (including the initial call)
- **THEN** the caller SHALL receive the error from the last failed attempt

### Requirement: withErrorNormalization middleware wraps RPC errors
The package SHALL provide a `withErrorNormalization()` built-in middleware that wraps raw JSON-RPC errors into typed `BridgeError` instances.

Middleware-level error normalization SHALL preserve structured error fields (`code`, `message`, and optional `data`) whenever present and SHALL support deterministic global handling hooks for centralized retry/reporting strategy implementation.

#### Scenario: RPC error is wrapped in BridgeError
- **WHEN** `withErrorNormalization()` is registered and a bridge call returns a JSON-RPC error
- **THEN** the error SHALL be wrapped in a `BridgeError` with `code`, `message`, and optional `data` properties

#### Scenario: Global error hook observes normalized errors before rethrow
- **WHEN** normalized bridge errors are produced in middleware pipeline
- **THEN** configured global error hooks SHALL receive normalized error context before the error is rethrown to the caller

#### Scenario: Non-RPC errors pass through unchanged
- **WHEN** `withErrorNormalization()` is registered and a non-RPC error occurs (e.g., network failure)
- **THEN** the original error SHALL propagate without wrapping

