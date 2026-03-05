namespace Agibuild.Fulora;

/// <summary>
/// Drag-and-drop effect flags for native ↔ web interoperability.
/// </summary>
[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    All = Copy | Move | Link
}

/// <summary>
/// Payload for drag-and-drop operations between native and web contexts.
/// </summary>
public sealed record DragDropPayload
{
    public IReadOnlyList<FileDropInfo>? Files { get; init; }
    public string? Text { get; init; }
    public string? Html { get; init; }
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
    public required DragDropPayload Payload { get; init; }
    public required DragDropEffects AllowedEffects { get; init; }
    public DragDropEffects Effect { get; set; } = DragDropEffects.Copy;
    public double X { get; init; }
    public double Y { get; init; }
}

/// <summary>
/// Event args for drop completion.
/// </summary>
public sealed class DropEventArgs : EventArgs
{
    public required DragDropPayload Payload { get; init; }
    public DragDropEffects Effect { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
}
