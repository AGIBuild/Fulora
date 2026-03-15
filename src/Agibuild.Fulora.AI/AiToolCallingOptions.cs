using System.ComponentModel.DataAnnotations;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Configuration options for AI tool-calling loop.
/// </summary>
public sealed class AiToolCallingOptions
{
    /// <summary>
    /// Maximum number of tool-calling iterations per request.
    /// Prevents infinite loops when the LLM keeps requesting tool calls.
    /// Default: 10.
    /// </summary>
    [Range(1, 100)]
    public int MaxIterations { get; set; } = 10;
}
