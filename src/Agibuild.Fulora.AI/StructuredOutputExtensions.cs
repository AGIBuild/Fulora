using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Extension methods for structured (typed) AI completion with automatic
/// retry-with-error-feedback on validation failures.
/// </summary>
public static class StructuredOutputExtensions
{
    /// <summary>Default maximum retry attempts for structured output validation.</summary>
    public const int DefaultMaxRetries = 3;

    /// <summary>
    /// Completes an AI request with a typed response. The response is deserialized
    /// from JSON. On validation failure, the error is fed back to the model for retry.
    /// </summary>
    /// <typeparam name="T">The expected response type (must be JSON-serializable).</typeparam>
    public static async Task<T> CompleteAsync<T>(
        this IChatClient client,
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        int maxRetries = DefaultMaxRetries,
        JsonSerializerOptions? jsonOptions = null,
        CancellationToken cancellationToken = default)
    {
        jsonOptions ??= AIJsonUtilities.DefaultOptions;
        var messageList = messages.ToList();

        options ??= new ChatOptions();
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(jsonOptions);

        string? lastRawResponse = null;
        string? lastError = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0 && lastError is not null)
            {
                messageList.Add(new ChatMessage(ChatRole.Assistant, lastRawResponse ?? ""));
                messageList.Add(new ChatMessage(ChatRole.User,
                    $"The previous response was not valid JSON matching the expected schema. Error: {lastError}\nPlease fix and respond with valid JSON only."));
            }

            var response = await client.GetResponseAsync(messageList, options, cancellationToken);
            var text = response.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                lastRawResponse = text;
                lastError = "Response was empty.";
                continue;
            }

            try
            {
                var result = JsonSerializer.Deserialize<T>(text, jsonOptions);
                if (result is null)
                {
                    lastRawResponse = text;
                    lastError = $"Deserialized result was null for type {typeof(T).Name}.";
                    continue;
                }
                return result;
            }
            catch (JsonException ex)
            {
                lastRawResponse = text;
                lastError = ex.Message;
            }
        }

        throw new AiStructuredOutputException(
            $"Failed to get valid structured output of type {typeof(T).Name} after {maxRetries + 1} attempts.",
            lastRawResponse,
            lastError);
    }
}
