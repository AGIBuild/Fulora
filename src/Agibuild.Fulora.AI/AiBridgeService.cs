using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Implementation of <see cref="IAiBridgeService"/> that delegates to the
/// AI middleware pipeline (ContentGate → Resilience → Metering → Provider).
/// </summary>
public sealed class AiBridgeService : IAiBridgeService
{
    private readonly IAiProviderRegistry _providers;
    private readonly IAiPayloadStore _payloadStore;
    private readonly IAiToolRegistry _toolRegistry;
    private readonly IAiConversationManager _conversationManager;
    private readonly AiToolCallingOptions _toolCallingOptions;

    /// <summary>Initializes a new instance.</summary>
    public AiBridgeService(
        IAiProviderRegistry providers,
        IAiPayloadStore payloadStore,
        IAiToolRegistry toolRegistry,
        IAiConversationManager conversationManager,
        IOptions<AiToolCallingOptions> toolCallingOptions)
    {
        _providers = providers;
        _payloadStore = payloadStore;
        _toolRegistry = toolRegistry;
        _conversationManager = conversationManager;
        _toolCallingOptions = toolCallingOptions.Value;
    }

    /// <inheritdoc />
    public async Task<AiChatResult> Complete(AiChatRequest request)
    {
        var client = _providers.GetChatClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);
        var options = BuildOptions(request.ModelId);
        var response = await client.GetResponseAsync(messages, options);
        return ToResult(response);
    }

    /// <inheritdoc />
    public async Task<string> CompleteTyped(AiTypedChatRequest request)
    {
        var client = _providers.GetChatClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);

        var schemaElement = JsonDocument.Parse(request.JsonSchema).RootElement;
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaElement)
        };

        var response = await client.GetResponseAsync(messages, options);
        return response.Text ?? "{}";
    }

    /// <inheritdoc />
    public Task<string[]> ListProviders()
    {
        return Task.FromResult(_providers.ChatClientNames.ToArray());
    }

    /// <inheritdoc />
    public Task<string> UploadBlob(string base64Data, string mimeType, string? name)
    {
        var data = Convert.FromBase64String(base64Data);
        var payload = new AiMediaPayload { Data = data, MimeType = mimeType, Name = name };
        var blobId = _payloadStore.Store(payload);
        return Task.FromResult(blobId);
    }

    /// <inheritdoc />
    public Task<string?> FetchBlob(string blobId)
    {
        var payload = _payloadStore.Fetch(blobId);
        if (payload is null) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(Convert.ToBase64String(payload.Data));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamCompletion(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = _providers.GetChatClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);
        var options = BuildOptions(request.ModelId);

        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    /// <inheritdoc />
    public async Task<AiChatResult> RunWithTools(AiChatRequest request)
    {
        var client = GetToolCallingClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);
        var options = BuildToolOptions(request.ModelId);
        var response = await client.GetResponseAsync(messages, options);
        return ToResult(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamWithTools(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = GetToolCallingClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);
        var options = BuildToolOptions(request.ModelId);

        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    /// <inheritdoc />
    public Task<string> CreateConversation(AiConversationCreateRequest request)
    {
        var id = _conversationManager.CreateConversation(request.SystemPrompt);
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public async Task<AiChatResult> SendMessage(AiConversationMessageRequest request)
    {
        _conversationManager.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.User, request.Message));

        var messages = _conversationManager.GetMessages(request.ConversationId);
        var client = request.UseTools
            ? GetToolCallingClient(request.Provider)
            : _providers.GetChatClient(request.Provider);
        var options = request.UseTools
            ? BuildToolOptions(request.ModelId)
            : BuildOptions(request.ModelId);

        var response = await client.GetResponseAsync(messages, options);
        var assistantText = response.Text ?? "";

        _conversationManager.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.Assistant, assistantText));

        return ToResult(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamMessage(
        AiConversationMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _conversationManager.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.User, request.Message));

        var messages = _conversationManager.GetMessages(request.ConversationId);
        var client = request.UseTools
            ? GetToolCallingClient(request.Provider)
            : _providers.GetChatClient(request.Provider);
        var options = request.UseTools
            ? BuildToolOptions(request.ModelId)
            : BuildOptions(request.ModelId);

        var fullResponse = new System.Text.StringBuilder();

        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Text is { Length: > 0 } text)
            {
                fullResponse.Append(text);
                yield return text;
            }
        }

        _conversationManager.AddMessage(request.ConversationId,
            new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
    }

    /// <inheritdoc />
    public Task<AiConversationHistory> GetHistory(string conversationId)
    {
        var messages = _conversationManager.GetAllMessages(conversationId);
        var history = new AiConversationHistory
        {
            ConversationId = conversationId,
            Messages = messages.Select(m => new AiHistoryMessage
            {
                Role = m.Role.Value,
                Text = m.Text ?? ""
            }).ToArray()
        };
        return Task.FromResult(history);
    }

    /// <inheritdoc />
    public Task DeleteConversation(string conversationId)
    {
        _conversationManager.RemoveConversation(conversationId);
        return Task.CompletedTask;
    }

    private FunctionInvokingChatClient GetToolCallingClient(string? providerName)
    {
        var inner = _providers.GetChatClient(providerName);
        var maxIterations = _toolCallingOptions.MaxIterations;
        return new FunctionInvokingChatClient(inner) { MaximumIterationsPerRequest = maxIterations };
    }

    private ChatOptions BuildToolOptions(string? modelId)
    {
        var options = new ChatOptions();
        if (modelId is not null) options.ModelId = modelId;
        var tools = _toolRegistry.Tools;
        if (tools.Count > 0)
            options.Tools = [.. tools];
        return options;
    }

    private static ChatOptions? BuildOptions(string? modelId)
    {
        return modelId is not null ? new ChatOptions { ModelId = modelId } : null;
    }

    private static List<ChatMessage> BuildMessages(string? systemPrompt, string userMessage)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        messages.Add(new ChatMessage(ChatRole.User, userMessage));
        return messages;
    }

    private static AiChatResult ToResult(ChatResponse response)
    {
        return new AiChatResult
        {
            Text = response.Text ?? "",
            ModelId = response.ModelId,
            PromptTokens = (int?)response.Usage?.InputTokenCount,
            CompletionTokens = (int?)response.Usage?.OutputTokenCount,
        };
    }
}
