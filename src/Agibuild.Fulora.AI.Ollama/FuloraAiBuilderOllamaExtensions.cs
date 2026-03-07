using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Agibuild.Fulora.AI.Ollama;

/// <summary>
/// Extension methods for registering Ollama as an AI provider in the Fulora AI pipeline.
/// </summary>
public static class FuloraAiBuilderOllamaExtensions
{
    private static readonly Uri DefaultEndpoint = new("http://localhost:11434");

    /// <summary>
    /// Registers an Ollama-backed <see cref="IChatClient"/> with the given name.
    /// </summary>
    /// <param name="builder">The Fulora AI builder.</param>
    /// <param name="name">Provider name for named resolution.</param>
    /// <param name="endpoint">Ollama API endpoint. Defaults to <c>http://localhost:11434</c>.</param>
    /// <param name="model">Model name (e.g. "llama3"). If null, OllamaSharp uses its default.</param>
    public static FuloraAiBuilder AddOllama(
        this FuloraAiBuilder builder,
        string name,
        Uri? endpoint = null,
        string? model = null)
    {
        var client = CreateOllamaClient(endpoint ?? DefaultEndpoint, model);
        builder.AddChatClient(name, client);
        return builder;
    }

    internal static IChatClient CreateOllamaClient(Uri endpoint, string? model)
    {
        var apiClient = new OllamaApiClient(endpoint, model ?? "");
        return apiClient;
    }
}
