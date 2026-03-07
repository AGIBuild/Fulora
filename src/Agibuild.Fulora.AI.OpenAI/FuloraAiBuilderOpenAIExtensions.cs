using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Agibuild.Fulora.AI.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI as an AI provider in the Fulora AI pipeline.
/// </summary>
public static class FuloraAiBuilderOpenAIExtensions
{
    /// <summary>
    /// Registers an OpenAI-backed <see cref="IChatClient"/> with the given name.
    /// </summary>
    /// <param name="builder">The Fulora AI builder.</param>
    /// <param name="name">Provider name for named resolution.</param>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (e.g. "gpt-4o"). Defaults to "gpt-4o-mini".</param>
    /// <param name="endpoint">Custom OpenAI-compatible endpoint. Null uses the default OpenAI API.</param>
    public static FuloraAiBuilder AddOpenAI(
        this FuloraAiBuilder builder,
        string name,
        string apiKey,
        string model = "gpt-4o-mini",
        Uri? endpoint = null)
    {
        var client = CreateOpenAIClient(apiKey, model, endpoint);
        builder.AddChatClient(name, client);
        return builder;
    }

    internal static IChatClient CreateOpenAIClient(string apiKey, string model, Uri? endpoint)
    {
        var options = new OpenAIClientOptions();
        if (endpoint is not null)
            options.Endpoint = endpoint;

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        return openAiClient.GetChatClient(model).AsIChatClient();
    }
}
