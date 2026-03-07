## MODIFIED Requirements

### Requirement: Sample uses Fulora AI module for provider management

The `avalonia-ai-chat` sample SHALL use `AddFuloraAi()` and provider packages instead of directly constructing `IChatClient` instances.

#### Scenario: Sample starts with Ollama provider
- **GIVEN** `AI__PROVIDER=ollama` is set (or Ollama is auto-detected)
- **WHEN** the sample app starts
- **THEN** the app SHALL register an Ollama provider via `ai.AddOllama()` and use the Fulora AI middleware pipeline

#### Scenario: Sample starts with no AI backend
- **GIVEN** no AI backend is configured or reachable
- **WHEN** the sample app starts
- **THEN** the app SHALL fall back to an echo-mode `IChatClient` registered as the default provider
- **AND** a banner SHALL inform the user that AI is in demo mode

### Requirement: Sample works without API key via echo/Ollama fallback

The sample SHALL function without any external AI service by falling back to an echo-mode implementation.

#### Scenario: No AI backend configured
- **GIVEN** no `AI__PROVIDER` environment variable is set and Ollama is not reachable
- **WHEN** the sample app starts
- **THEN** the app SHALL use an echo-mode implementation that streams back the prompt character by character
- **AND** a banner SHALL inform the user that AI is in demo mode
