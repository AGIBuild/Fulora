## 0. Sequencing gate

- [x] 0.1 Confirm this change executes after `runtime-architecture-decomposition` baseline stabilization

## 1. Options pattern adoption

- [ ] 1.1 Add DataAnnotations validation attributes to AI options types (AiResilienceOptions, AiMeteringOptions, AiToolCallingOptions, AiConversationOptions)
- [ ] 1.2 Update FuloraAiBuilder.Build() to register options via AddOptions<T>().ValidateDataAnnotations()
- [ ] 1.3 Update AI service constructors to accept IOptions<T> instead of raw options
- [ ] 1.4 Add unit tests for options validation (invalid values trigger OptionsValidationException)

## 2. HttpClient factory fix

- [ ] 2.1 Register named HttpClient "FuloraRemoteConfig" in AddFulora()
- [ ] 2.2 Update AddRemoteConfig to resolve HttpClient from IHttpClientFactory
- [ ] 2.3 Remove `new HttpClient()` fallback
- [ ] 2.4 Add unit test verifying IHttpClientFactory usage

## 3. Interface segregation

- [ ] 3.1 Define IWebViewNavigation, IWebViewScript, IWebViewBridge, IWebViewFeatures in Core
- [ ] 3.2 Redefine IWebView as composite of the four sub-interfaces
- [ ] 3.3 Verify WebViewCore compiles without changes (already implements all members)
- [ ] 3.4 Define IAiChatService, IAiToolService, IAiBlobService, IAiConversationService, IAiProviderService in AI
- [ ] 3.5 Redefine IAiBridgeService as composite
- [ ] 3.6 Verify AiBridgeService compiles without changes
- [ ] 3.7 Add compilation tests ensuring backward compatibility

## 4. Error model unification

- [ ] 4.1 Create FuloraException base class with ErrorCode in Core
- [ ] 4.2 Define error code constants (bridge errors, navigation errors, AI errors)
- [ ] 4.3 Reparent AiContentBlockedException, AiBudgetExceededException, AiStructuredOutputException to extend FuloraException
- [ ] 4.4 Audit and fix empty catch blocks in Runtime (add logging or rethrow)
- [ ] 4.5 Audit and fix empty catch blocks in AI layer
- [ ] 4.6 Add error mapping in WebViewRpcService to convert FuloraException to structured JSON-RPC errors
- [ ] 4.7 Add unit tests for error code mapping

## 5. Solution consistency

- [ ] 5.1 Add missing projects to Agibuild.Fulora.sln (Auth.OAuth, Plugin.Database, Plugin.LocalStorage, Plugin.HttpClient)
- [ ] 5.2 Create SolutionConsistencyGovernance target in build system
- [ ] 5.3 Add to Ci target dependency chain
- [ ] 5.4 Verify `dotnet build Agibuild.Fulora.sln` succeeds with all projects

## 6. Verification

- [ ] 6.1 Run full unit test suite
- [ ] 6.2 Run coverage check (96% line / 93% branch)
- [ ] 6.3 Verify all governance targets pass
- [ ] 6.4 Verify OpenSpec strict validation passes
