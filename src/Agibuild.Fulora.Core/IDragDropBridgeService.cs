namespace Agibuild.Fulora;

/// <summary>
/// Bridge service that exposes drag-and-drop events to JavaScript.
/// Events are delivered as streaming callbacks via bridge import proxies.
/// </summary>
[JsExport]
public interface IDragDropBridgeService
{
    /// <summary>Returns the payload of the most recent drop event, or null if none occurred.</summary>
    Task<DragDropPayload?> GetLastDropPayloadAsync(CancellationToken ct = default);

    /// <summary>Indicates whether the current platform supports drag-and-drop operations.</summary>
    Task<bool> IsDragDropSupportedAsync(CancellationToken ct = default);
}
