using Agibuild.Fulora;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Bridge service exposing AI capabilities to JavaScript.
/// Methods are callable from the JS side via the Fulora bridge.
/// </summary>
[JsExport]
public interface IAiBridgeService
{
    /// <summary>Sends a chat completion request and returns the full response text.</summary>
    Task<AiChatResult> Complete(AiChatRequest request);

    /// <summary>Sends a typed chat completion request that returns structured JSON.</summary>
    Task<string> CompleteTyped(AiTypedChatRequest request);

    /// <summary>Lists available AI provider names.</summary>
    Task<string[]> ListProviders();

    /// <summary>Stores a binary payload and returns a blob ID for later use.</summary>
    Task<string> UploadBlob(string base64Data, string mimeType, string? name);

    /// <summary>Retrieves a stored blob as Base64.</summary>
    Task<string?> FetchBlob(string blobId);

    /// <summary>Streams chat completion tokens as they are generated.</summary>
    IAsyncEnumerable<string> StreamCompletion(AiChatRequest request, CancellationToken cancellationToken = default);
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
