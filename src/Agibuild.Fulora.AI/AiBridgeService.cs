using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Implementation of <see cref="IAiBridgeService"/> that delegates to the
/// AI middleware pipeline (ContentGate → Resilience → Metering → Provider).
/// </summary>
public sealed class AiBridgeService : IAiBridgeService
{
    private readonly IAiProviderRegistry _providers;
    private readonly IAiPayloadStore _payloadStore;

    public AiBridgeService(IAiProviderRegistry providers, IAiPayloadStore payloadStore)
    {
        _providers = providers;
        _payloadStore = payloadStore;
    }

    public async Task<AiChatResult> Complete(AiChatRequest request)
    {
        var client = _providers.GetChatClient(request.Provider);
        var messages = BuildMessages(request.SystemPrompt, request.Message);

        var options = request.ModelId is not null ? new ChatOptions { ModelId = request.ModelId } : null;
        var response = await client.GetResponseAsync(messages, options);

        return new AiChatResult
        {
            Text = response.Text ?? "",
            ModelId = response.ModelId,
            PromptTokens = (int?)response.Usage?.InputTokenCount,
            CompletionTokens = (int?)response.Usage?.OutputTokenCount,
        };
    }

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

    public Task<string[]> ListProviders()
    {
        return Task.FromResult(_providers.ChatClientNames.ToArray());
    }

    public Task<string> UploadBlob(string base64Data, string mimeType, string? name)
    {
        var data = Convert.FromBase64String(base64Data);
        var payload = new AiMediaPayload { Data = data, MimeType = mimeType, Name = name };
        var blobId = _payloadStore.Store(payload);
        return Task.FromResult(blobId);
    }

    public Task<string?> FetchBlob(string blobId)
    {
        var payload = _payloadStore.Fetch(blobId);
        if (payload is null) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(Convert.ToBase64String(payload.Data));
    }

    private static List<ChatMessage> BuildMessages(string? systemPrompt, string userMessage)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        messages.Add(new ChatMessage(ChatRole.User, userMessage));
        return messages;
    }
}
