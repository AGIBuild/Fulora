## Context

The framework uses raw singleton registration for options, wide interfaces that bundle unrelated concerns, inconsistent error handling across layers, and a .sln that doesn't include all Nuke-built projects. These issues independently reduce maintainability but share the common theme of "framework internal quality."

## Goals / Non-Goals

**Goals:**
- Adopt IOptions<T> pattern with validation for AI configuration
- Fix HttpClient construction anti-pattern
- Split IWebView and IAiBridgeService into domain interfaces with backward-compatible composites
- Establish FuloraException base with error code taxonomy
- Align .sln with Nuke build scope

**Non-Goals:**
- Breaking the AddFulora() / FuloraAiBuilder fluent API
- Forcing consumers to adopt split interfaces (they remain opt-in)
- Implementing retry/recovery strategies (just unifying taxonomy)

## Decisions

1. **Options migration strategy**: FuloraAiBuilder continues to expose the fluent API. Internally, `Build()` registers options via `services.AddOptions<T>().Configure(o => { copy from builder })`. Consumers can alternatively bind from IConfiguration. DataAnnotations validation is added to all options types.

2. **HttpClient fix**: Register a named HttpClient "FuloraRemoteConfig" during AddFulora(). AddRemoteConfig resolves it from IHttpClientFactory. This is a one-line fix with high socket-safety impact.

3. **Interface split approach**: Define new sub-interfaces in Core (IWebViewNavigation, IWebViewScript, etc.). Redefine IWebView as `IWebView : IWebViewNavigation, IWebViewScript, IWebViewBridge, IWebViewFeatures`. WebViewCore already implements all members — no implementation change needed. IAiBridgeService follows the same pattern.

4. **FuloraException hierarchy**: `FuloraException(string errorCode, string message, Exception? inner)` in Core. Subclasses: existing `AiContentBlockedException`, `AiBudgetExceededException`, `AiStructuredOutputException` extend it. New `BridgeCallException`, `WebViewNavigationException`. ErrorCode maps to JSON-RPC error codes.

5. **Solution consistency governance**: New target `SolutionConsistencyGovernance` that parses .sln and compares with Nuke's project list. Reports missing projects as GovernanceFailure.

## Risks / Trade-offs

- **IOptions migration**: Consumers that directly construct options objects and register as singletons will still work (TryAdd pattern). No breaking change.
- **Interface split binary compatibility**: Adding base interfaces to IWebView is a source-compatible change but technically a binary-breaking change for assemblies compiled against the old IWebView. Since we control all implementations and this is pre-2.0, acceptable.
- **FuloraException adoption**: Existing exceptions (AiContentBlockedException etc.) need to be reparented. This is a binary-breaking change for anyone catching by type — but these types are in the AI package which is explicitly noted as thin integration wrapper.
