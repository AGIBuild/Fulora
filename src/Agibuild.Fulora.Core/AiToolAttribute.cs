namespace Agibuild.Fulora;

/// <summary>
/// Marks a <see cref="JsExportAttribute"/>-decorated interface or method for AI tool schema generation.
/// The source generator will emit an OpenAI-compatible function-calling JSON schema.
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AiToolAttribute : Attribute
{
    /// <summary>
    /// Optional group name for organizing tools.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Optional description override. If not set, the XML doc summary is used.
    /// </summary>
    public string? Description { get; set; }
}
