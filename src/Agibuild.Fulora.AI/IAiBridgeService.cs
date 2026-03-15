using Agibuild.Fulora;

namespace Agibuild.Fulora.AI;

/// <summary>Chat completion operations (single-shot and streaming).</summary>
public interface IAiChatService
{
    /// <summary>Sends a chat completion request and returns the full response text.</summary>
    Task<AiChatResult> Complete(AiChatRequest request);

    /// <summary>Sends a typed chat completion request that returns structured JSON.</summary>
    Task<string> CompleteTyped(AiTypedChatRequest request);

    /// <summary>Streams chat completion tokens as they are generated.</summary>
    IAsyncEnumerable<string> StreamCompletion(AiChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Tool-calling loop operations.</summary>
public interface IAiToolService
{
    /// <summary>Runs a chat completion with registered tools (tool-calling loop).</summary>
    Task<AiChatResult> RunWithTools(AiChatRequest request);

    /// <summary>Streams a chat completion with registered tools.</summary>
    IAsyncEnumerable<string> StreamWithTools(AiChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Binary payload storage for AI multimodal inputs.</summary>
public interface IAiBlobService
{
    /// <summary>Stores a binary payload and returns a blob ID for later use.</summary>
    Task<string> UploadBlob(string base64Data, string mimeType, string? name);

    /// <summary>Retrieves a stored blob as Base64.</summary>
    Task<string?> FetchBlob(string blobId);
}

/// <summary>Conversation session management.</summary>
public interface IAiConversationService
{
    /// <summary>Creates a new conversation session.</summary>
    Task<string> CreateConversation(AiConversationCreateRequest request);

    /// <summary>Sends a message in a conversation and returns the response.</summary>
    Task<AiChatResult> SendMessage(AiConversationMessageRequest request);

    /// <summary>Sends a message in a conversation and streams the response.</summary>
    IAsyncEnumerable<string> StreamMessage(AiConversationMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets the conversation history.</summary>
    Task<AiConversationHistory> GetHistory(string conversationId);

    /// <summary>Deletes a conversation.</summary>
    Task DeleteConversation(string conversationId);
}

/// <summary>Provider enumeration.</summary>
public interface IAiProviderService
{
    /// <summary>Lists available AI provider names.</summary>
    Task<string[]> ListProviders();
}

/// <summary>
/// Composite bridge service exposing all AI capabilities to JavaScript.
/// Methods are callable from the JS side via the Fulora bridge.
/// </summary>
[JsExport]
public interface IAiBridgeService : IAiChatService, IAiToolService, IAiBlobService, IAiConversationService, IAiProviderService
{
}

/// <summary>Chat completion request from JS.</summary>
public sealed record AiChatRequest
{
    /// <summary>The user message text.</summary>
    public required string Message { get; init; }

    /// <summary>Optional system prompt.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Optional provider name (null = default).</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model ID override.</summary>
    public string? ModelId { get; init; }
}

/// <summary>Chat completion result returned to JS.</summary>
public sealed record AiChatResult
{
    /// <summary>The assistant's response text.</summary>
    public required string Text { get; init; }

    /// <summary>Model ID used.</summary>
    public string? ModelId { get; init; }

    /// <summary>Prompt tokens used.</summary>
    public int? PromptTokens { get; init; }

    /// <summary>Completion tokens used.</summary>
    public int? CompletionTokens { get; init; }
}

/// <summary>Request to create a conversation session.</summary>
public sealed record AiConversationCreateRequest
{
    /// <summary>Optional system prompt for the conversation.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Optional provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model ID override.</summary>
    public string? ModelId { get; init; }
}

/// <summary>Request to send a message in a conversation.</summary>
public sealed record AiConversationMessageRequest
{
    /// <summary>The conversation ID.</summary>
    public required string ConversationId { get; init; }

    /// <summary>The user message text.</summary>
    public required string Message { get; init; }

    /// <summary>Optional provider name override (null = use conversation default).</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model ID override.</summary>
    public string? ModelId { get; init; }

    /// <summary>Whether to use registered tools. Default: false.</summary>
    public bool UseTools { get; init; }
}

/// <summary>Conversation history returned to JS.</summary>
public sealed record AiConversationHistory
{
    /// <summary>The conversation ID.</summary>
    public required string ConversationId { get; init; }

    /// <summary>Messages in chronological order.</summary>
    public required AiHistoryMessage[] Messages { get; init; }
}

/// <summary>A single message in conversation history.</summary>
public sealed record AiHistoryMessage
{
    /// <summary>Role: "system", "user", or "assistant".</summary>
    public required string Role { get; init; }

    /// <summary>Message text content.</summary>
    public required string Text { get; init; }
}

/// <summary>Typed (structured output) chat request from JS.</summary>
public sealed record AiTypedChatRequest
{
    /// <summary>The user message text.</summary>
    public required string Message { get; init; }

    /// <summary>JSON Schema string defining the expected response shape.</summary>
    public required string JsonSchema { get; init; }

    /// <summary>Optional system prompt.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Optional provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Max retries for structured output validation. Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;
}
