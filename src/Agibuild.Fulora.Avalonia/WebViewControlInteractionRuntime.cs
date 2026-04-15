namespace Agibuild.Fulora;

/// <summary>
/// Maintains the per-event aggregates for the five interaction events that WebView
/// exposes outside the core event pipeline. Handlers are stored here and routed to
/// the core via the stable wrappers owned by <see cref="WebViewControlEventRuntime"/>.
/// </summary>
internal sealed class WebViewControlInteractionRuntime
{
    private EventHandler<ContextMenuRequestedEventArgs>? _contextMenuRequestedHandlers;
    private EventHandler<DragEventArgs>? _dragEnteredHandlers;
    private EventHandler<DragEventArgs>? _dragOverHandlers;
    private EventHandler<EventArgs>? _dragLeftHandlers;
    private EventHandler<DropEventArgs>? _dropCompletedHandlers;

    public EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequestedHandlers => _contextMenuRequestedHandlers;
    public EventHandler<DragEventArgs>? DragEnteredHandlers => _dragEnteredHandlers;
    public EventHandler<DragEventArgs>? DragOverHandlers => _dragOverHandlers;
    public EventHandler<EventArgs>? DragLeftHandlers => _dragLeftHandlers;
    public EventHandler<DropEventArgs>? DropCompletedHandlers => _dropCompletedHandlers;

    public void AddContextMenuRequestedHandler(EventHandler<ContextMenuRequestedEventArgs> handler)
        => AddToAggregate(ref _contextMenuRequestedHandlers, handler);

    public void RemoveContextMenuRequestedHandler(EventHandler<ContextMenuRequestedEventArgs> handler)
        => RemoveFromAggregate(ref _contextMenuRequestedHandlers, handler);

    public void AddDragEnteredHandler(EventHandler<DragEventArgs> handler)
        => AddToAggregate(ref _dragEnteredHandlers, handler);

    public void RemoveDragEnteredHandler(EventHandler<DragEventArgs> handler)
        => RemoveFromAggregate(ref _dragEnteredHandlers, handler);

    public void AddDragOverHandler(EventHandler<DragEventArgs> handler)
        => AddToAggregate(ref _dragOverHandlers, handler);

    public void RemoveDragOverHandler(EventHandler<DragEventArgs> handler)
        => RemoveFromAggregate(ref _dragOverHandlers, handler);

    public void AddDragLeftHandler(EventHandler<EventArgs> handler)
        => AddToAggregate(ref _dragLeftHandlers, handler);

    public void RemoveDragLeftHandler(EventHandler<EventArgs> handler)
        => RemoveFromAggregate(ref _dragLeftHandlers, handler);

    public void AddDropCompletedHandler(EventHandler<DropEventArgs> handler)
        => AddToAggregate(ref _dropCompletedHandlers, handler);

    public void RemoveDropCompletedHandler(EventHandler<DropEventArgs> handler)
        => RemoveFromAggregate(ref _dropCompletedHandlers, handler);

    private static void AddToAggregate<TEventArgs>(
        ref EventHandler<TEventArgs>? aggregate,
        EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(handler);
        aggregate += handler;
    }

    private static void RemoveFromAggregate<TEventArgs>(
        ref EventHandler<TEventArgs>? aggregate,
        EventHandler<TEventArgs> handler)
        where TEventArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(handler);
        aggregate -= handler;
    }
}
