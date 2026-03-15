## ADDED Requirements

### Requirement: IWebView composed from domain interfaces
IWebView SHALL be defined as a composite of domain-specific interfaces: IWebViewNavigation, IWebViewScript, IWebViewBridge, and IWebViewFeatures. The composite IWebView interface MUST remain for backward compatibility.

#### Scenario: Consumer uses domain interface
- **WHEN** a component only needs navigation capabilities
- **THEN** it MUST be able to depend on IWebViewNavigation without requiring the full IWebView

#### Scenario: Existing consumers unaffected
- **WHEN** existing code depends on IWebView
- **THEN** it MUST continue to compile and work without changes

### Requirement: IAiBridgeService composed from domain interfaces
IAiBridgeService SHALL be composed from IAiChatService, IAiToolService, IAiBlobService, IAiConversationService, and IAiProviderService. The composite interface MUST remain for backward compatibility.

#### Scenario: Chat-only consumer
- **WHEN** a component only needs chat completion
- **THEN** it MUST be able to depend on IAiChatService alone
