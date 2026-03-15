## Why

Three interconnected areas need modernization: (1) Configuration uses raw singleton registration instead of the standard IOptions pattern, preventing config reload and validation; (2) Core interfaces like IWebView (~25 members) and IAiBridgeService (13 methods) violate Interface Segregation Principle; (3) Error handling is inconsistent — empty catch blocks, no unified error taxonomy, and varying propagation strategies across Runtime/AI/Bridge layers. Additionally, AddRemoteConfig creates `new HttpClient()` bypassing IHttpClientFactory, risking socket exhaustion. Projects built by Nuke but missing from the main .sln reduce IDE discoverability. Addressing these together modernizes the framework's internal quality for the post-GA maintenance phase.

## Non-goals

- Breaking the existing AddFulora() / FuloraAiBuilder fluent API surface
- Forcing consumers to adopt split interfaces immediately
- Implementing new error recovery strategies (just unifying the taxonomy)

## What Changes

- **Options pattern adoption**: Introduce `IOptions<T>` with `ValidateDataAnnotations()` for `AiResilienceOptions`, `AiMeteringOptions`, `AiToolCallingOptions`, `AiConversationOptions`. FuloraAiBuilder continues to work but internally registers via Options pattern.
- **HttpClient fix**: Replace `new HttpClient()` in AddRemoteConfig with IHttpClientFactory named client.
- **Interface segregation**: Split `IWebView` into `IWebViewNavigation`, `IWebViewScript`, `IWebViewBridge`, `IWebViewFeatures` with `IWebView` as composite. Split `IAiBridgeService` into `IAiChatService`, `IAiToolService`, `IAiBlobService`, `IAiConversationService`, `IAiProviderService`. Existing interfaces retained as composites for backward compatibility.
- **Error model unification**: Define `FuloraException` base with error code taxonomy. Map Runtime/AI/Bridge exceptions to structured codes aligned with bridge `code/message/data`. Remove empty catch blocks, add minimum logging.
- **Solution consistency**: Add missing projects (Auth.OAuth, Plugin.Database, Plugin.LocalStorage, Plugin.HttpClient) to main .sln. Add solution-consistency governance target.

## Execution Order

- This change is the second modernization stage and starts only after `runtime-architecture-decomposition` reaches verification closure.
- Rationale: options/interface/error-model modernization should build on top of a stabilized Runtime decomposition baseline.

## Capabilities

### New Capabilities
- `options-pattern-conventions`: IOptions usage rules, validation, naming, and migration path
- `interface-segregation-policy`: ISP rules for core interfaces with backward-compatible composite strategy
- `error-handling-contracts`: C#-side error taxonomy, propagation rules, and bridge error mapping
- `solution-structure-governance`: Rules for .sln ↔ build scope consistency

### Modified Capabilities
- `webview-di-integration`: AddFulora() internals transition to Options pattern while preserving fluent API

## Impact

- **DI/Config**: `ServiceCollectionExtensions.cs`, `FuloraAiBuilder.cs`, `FuloraAiServiceCollectionExtensions.cs`
- **Interfaces**: `IWebView` in Core (additive split), `IAiBridgeService` in AI (additive split)
- **Error handling**: `RuntimeBridgeService.cs`, `WebViewRpcService.cs`, `AiBridgeService.cs`, `ContentGateChatClient.cs`
- **Solution**: `Agibuild.Fulora.sln`, `build/Build.Governance.cs` (new governance target)
- **Tests**: New validation tests for options; interface split tests; error mapping tests
