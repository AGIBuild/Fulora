namespace Agibuild.Fulora;

/// <summary>
/// Drag-and-drop effect flags for native ↔ web interoperability.
/// </summary>
[Flags]
public enum DragDropEffects
{
    /// <summary>No drag-drop effect.</summary>
    None = 0,
    /// <summary>Copy the dragged data.</summary>
    Copy = 1,
    /// <summary>Move the dragged data.</summary>
    Move = 2,
    /// <summary>Create a link to the dragged data.</summary>
    Link = 4,
    /// <summary>Copy, move, or link.</summary>
    All = Copy | Move | Link
}

/// <summary>
/// Payload for drag-and-drop operations between native and web contexts.
/// </summary>
public sealed record DragDropPayload
{
    /// <summary>Dropped files, if any.</summary>
    public IReadOnlyList<FileDropInfo>? Files { get; init; }
    /// <summary>Plain text content, if any.</summary>
    public string? Text { get; init; }
    /// <summary>HTML content, if any.</summary>
    public string? Html { get; init; }
    /// <summary>URI content, if any.</summary>
    public string? Uri { get; init; }
}

/// <summary>
/// Metadata for a file in a drag-drop payload.
/// </summary>
public sealed record FileDropInfo(string Path, string? MimeType = null, long? Size = null);

/// <summary>
/// Event args for drag enter/over events. Effect can be set by the handler.
/// </summary>
public sealed class DragEventArgs : EventArgs
{
    /// <summary>The drag payload.</summary>
    public required DragDropPayload Payload { get; init; }
    /// <summary>Effects permitted by the drag source.</summary>
    public required DragDropEffects AllowedEffects { get; init; }
    /// <summary>The effect chosen by the drop handler.</summary>
    public DragDropEffects Effect { get; set; } = DragDropEffects.Copy;
    /// <summary>Horizontal position relative to the WebView.</summary>
    public double X { get; init; }
    /// <summary>Vertical position relative to the WebView.</summary>
    public double Y { get; init; }
}

/// <summary>
/// Event args for drop completion.
/// </summary>
public sealed class DropEventArgs : EventArgs
{
    /// <summary>The drop payload.</summary>
    public required DragDropPayload Payload { get; init; }
    /// <summary>The drop effect applied.</summary>
    public DragDropEffects Effect { get; init; }
    /// <summary>Horizontal position relative to the WebView.</summary>
    public double X { get; init; }
    /// <summary>Vertical position relative to the WebView.</summary>
    public double Y { get; init; }
}
