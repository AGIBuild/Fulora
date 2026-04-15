namespace Agibuild.Fulora;

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

    public void AddContextMenuRequestedHandler(IWebViewCoreControlEvents? core, EventHandler<ContextMenuRequestedEventArgs> handler)
        => AddHandler(ref _contextMenuRequestedHandlers, core, handler, static (events, value) => events.ContextMenuRequested += value);

    public void RemoveContextMenuRequestedHandler(IWebViewCoreControlEvents? core, EventHandler<ContextMenuRequestedEventArgs> handler)
        => RemoveHandler(ref _contextMenuRequestedHandlers, core, handler, static (events, value) => events.ContextMenuRequested -= value);

    public void AddDragEnteredHandler(IWebViewCoreControlEvents? core, EventHandler<DragEventArgs> handler)
        => AddHandler(ref _dragEnteredHandlers, core, handler, static (events, value) => events.DragEntered += value);

    public void RemoveDragEnteredHandler(IWebViewCoreControlEvents? core, EventHandler<DragEventArgs> handler)
        => RemoveHandler(ref _dragEnteredHandlers, core, handler, static (events, value) => events.DragEntered -= value);

    public void AddDragOverHandler(IWebViewCoreControlEvents? core, EventHandler<DragEventArgs> handler)
        => AddHandler(ref _dragOverHandlers, core, handler, static (events, value) => events.DragOver += value);

    public void RemoveDragOverHandler(IWebViewCoreControlEvents? core, EventHandler<DragEventArgs> handler)
        => RemoveHandler(ref _dragOverHandlers, core, handler, static (events, value) => events.DragOver -= value);

    public void AddDragLeftHandler(IWebViewCoreControlEvents? core, EventHandler<EventArgs> handler)
        => AddHandler(ref _dragLeftHandlers, core, handler, static (events, value) => events.DragLeft += value);

    public void RemoveDragLeftHandler(IWebViewCoreControlEvents? core, EventHandler<EventArgs> handler)
        => RemoveHandler(ref _dragLeftHandlers, core, handler, static (events, value) => events.DragLeft -= value);

    public void AddDropCompletedHandler(IWebViewCoreControlEvents? core, EventHandler<DropEventArgs> handler)
        => AddHandler(ref _dropCompletedHandlers, core, handler, static (events, value) => events.DropCompleted += value);

    public void RemoveDropCompletedHandler(IWebViewCoreControlEvents? core, EventHandler<DropEventArgs> handler)
        => RemoveHandler(ref _dropCompletedHandlers, core, handler, static (events, value) => events.DropCompleted -= value);

    private static void AddHandler<TEventArgs>(
        ref EventHandler<TEventArgs>? aggregate,
        IWebViewCoreControlEvents? core,
        EventHandler<TEventArgs> handler,
        Action<IWebViewCoreControlEvents, EventHandler<TEventArgs>> attach)
        where TEventArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(handler);

        aggregate += handler;
        if (core is not null)
            attach(core, handler);
    }

    private static void RemoveHandler<TEventArgs>(
        ref EventHandler<TEventArgs>? aggregate,
        IWebViewCoreControlEvents? core,
        EventHandler<TEventArgs> handler,
        Action<IWebViewCoreControlEvents, EventHandler<TEventArgs>> detach)
        where TEventArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(handler);

        aggregate -= handler;
        if (core is not null)
            detach(core, handler);
    }
}
