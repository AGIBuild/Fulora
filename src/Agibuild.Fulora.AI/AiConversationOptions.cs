using System.ComponentModel.DataAnnotations;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Configuration options for AI conversation management.
/// </summary>
public sealed class AiConversationOptions
{
    /// <summary>
    /// Default maximum token budget for conversation history windowing.
    /// Messages beyond this budget are trimmed (oldest first, system prompt always retained).
    /// Default: 4096.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Time-to-live for inactive conversation sessions. Null means no expiry.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan? SessionTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Custom token estimation function. Input is text content, output is estimated token count.
    /// Default: <c>text.Length / 4</c> (industry-standard approximation for English).
    /// </summary>
    public Func<string, int> EstimateTokens { get; set; } = text => Math.Max(1, text.Length / 4);
}
