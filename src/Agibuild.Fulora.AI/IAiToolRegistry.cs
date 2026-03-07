using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Runtime registry for AI tools. Discovers <see cref="AiToolAttribute"/>-marked methods
/// and manages their function declarations for AI model tool-calling.
/// </summary>
public interface IAiToolRegistry
{
    /// <summary>All registered tool declarations.</summary>
    IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>Registers all <see cref="AiToolAttribute"/>-marked methods on the instance.</summary>
    void Register(object instance);

    /// <summary>Finds a tool by name.</summary>
    AIFunction? FindTool(string name);
}
