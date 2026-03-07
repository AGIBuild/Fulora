using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Registry for named AI providers. Supports multiple concurrent providers
/// with default fallback to the first registered.
/// </summary>
public interface IAiProviderRegistry
{
    /// <summary>
    /// Resolves a chat client by name, or the default if <paramref name="name"/> is null.
    /// </summary>
    IChatClient GetChatClient(string? name = null);

    /// <summary>
    /// Resolves an embedding generator by name, or the default if <paramref name="name"/> is null.
    /// </summary>
    IEmbeddingGenerator<string, Embedding<float>> GetEmbeddingGenerator(string? name = null);

    /// <summary>Returns all registered chat client names.</summary>
    IReadOnlyList<string> ChatClientNames { get; }

    /// <summary>Returns all registered embedding generator names.</summary>
    IReadOnlyList<string> EmbeddingGeneratorNames { get; }
}
