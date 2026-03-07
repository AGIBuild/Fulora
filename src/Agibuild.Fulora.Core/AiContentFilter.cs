namespace Agibuild.Fulora;

/// <summary>
/// Pluggable content filter for AI input/output safety.
/// Implementations are composed into an ordered pipeline via DI.
/// </summary>
public interface IAiContentFilter
{
    /// <summary>
    /// Filters user input before sending to the AI provider.
    /// </summary>
    Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters AI provider output before returning to the caller.
    /// </summary>
    Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a content filter operation.
/// </summary>
public sealed class ContentFilterResult
{
    /// <summary>The action to take.</summary>
    public ContentFilterAction Action { get; }

    /// <summary>Reason for blocking (when Action is Block).</summary>
    public string? Reason { get; }

    /// <summary>Transformed content (when Action is Transform).</summary>
    public string? TransformedContent { get; }

    private ContentFilterResult(ContentFilterAction action, string? reason = null, string? transformedContent = null)
    {
        Action = action;
        Reason = reason;
        TransformedContent = transformedContent;
    }

    /// <summary>Allow the content through unchanged.</summary>
    public static ContentFilterResult Allow => new(ContentFilterAction.Allow);

    /// <summary>Block the content with a reason.</summary>
    public static ContentFilterResult Block(string reason) => new(ContentFilterAction.Block, reason: reason);

    /// <summary>Replace the content with transformed version.</summary>
    public static ContentFilterResult Transform(string modifiedContent) => new(ContentFilterAction.Transform, transformedContent: modifiedContent);
}

/// <summary>
/// The action a content filter takes on content.
/// </summary>
public enum ContentFilterAction
{
    /// <summary>Allow content through unchanged.</summary>
    Allow,

    /// <summary>Block content entirely.</summary>
    Block,

    /// <summary>Replace content with transformed version.</summary>
    Transform
}
