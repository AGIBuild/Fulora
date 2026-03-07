using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Default implementation of <see cref="IAiProviderRegistry"/>. Stores named providers
/// and resolves by name with default-first fallback.
/// </summary>
public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly Dictionary<string, IChatClient> _chatClients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerators = new(StringComparer.OrdinalIgnoreCase);
    private string? _defaultChatClientName;
    private string? _defaultEmbeddingGeneratorName;

    public IReadOnlyList<string> ChatClientNames => [.. _chatClients.Keys];
    public IReadOnlyList<string> EmbeddingGeneratorNames => [.. _embeddingGenerators.Keys];

    public void RegisterChatClient(string name, IChatClient client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(client);
        _chatClients[name] = client;
        _defaultChatClientName ??= name;
    }

    public void RegisterEmbeddingGenerator(string name, IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(generator);
        _embeddingGenerators[name] = generator;
        _defaultEmbeddingGeneratorName ??= name;
    }

    public IChatClient GetChatClient(string? name = null)
    {
        var key = name ?? _defaultChatClientName
            ?? throw new InvalidOperationException("No AI chat client providers have been registered. Call AddFuloraAi() and register at least one chat client.");

        if (!_chatClients.TryGetValue(key, out var client))
            throw new InvalidOperationException($"AI chat client '{key}' is not registered. Available: [{string.Join(", ", _chatClients.Keys)}]");

        return client;
    }

    public IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(string? name = null)
    {
        var key = name ?? _defaultEmbeddingGeneratorName
            ?? throw new InvalidOperationException("No AI embedding generator providers have been registered. Call AddFuloraAi() and register at least one embedding generator.");

        if (!_embeddingGenerators.TryGetValue(key, out var generator))
            throw new InvalidOperationException($"AI embedding generator '{key}' is not registered. Available: [{string.Join(", ", _embeddingGenerators.Keys)}]");

        return generator;
    }
}
