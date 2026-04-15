using Avalonia;
using Avalonia.Controls;

namespace Agibuild.Fulora;

internal sealed class WebViewOverlayRuntime : IDisposable
{
    private readonly Func<WebViewOverlayHost> _createOverlayHost;
    private readonly Func<bool> _hasVisualRoot;
    private readonly Func<object?> _getTopLevelWindow;
    private readonly Action<EventHandler> _subscribeLayoutUpdated;
    private readonly Action<EventHandler> _unsubscribeLayoutUpdated;
    private readonly Action<object, EventHandler<PixelPointEventArgs>> _subscribeWindowPositionChanged;
    private readonly Action<object, EventHandler<PixelPointEventArgs>> _unsubscribeWindowPositionChanged;
    private readonly Action _refreshOverlayLayout;

    private WebViewOverlayHost? _overlayHost;
    private EventHandler? _layoutUpdatedHandler;
    private EventHandler<PixelPointEventArgs>? _windowPositionChangedHandler;
    private object? _positionChangedWindow;

    public WebViewOverlayRuntime(
        Func<WebViewOverlayHost> createOverlayHost,
        Func<bool> hasVisualRoot,
        Func<object?> getTopLevelWindow,
        Action<EventHandler> subscribeLayoutUpdated,
        Action<EventHandler> unsubscribeLayoutUpdated,
        Action<object, EventHandler<PixelPointEventArgs>> subscribeWindowPositionChanged,
        Action<object, EventHandler<PixelPointEventArgs>> unsubscribeWindowPositionChanged,
        Action refreshOverlayLayout)
    {
        _createOverlayHost = createOverlayHost ?? throw new ArgumentNullException(nameof(createOverlayHost));
        _hasVisualRoot = hasVisualRoot ?? throw new ArgumentNullException(nameof(hasVisualRoot));
        _getTopLevelWindow = getTopLevelWindow ?? throw new ArgumentNullException(nameof(getTopLevelWindow));
        _subscribeLayoutUpdated = subscribeLayoutUpdated ?? throw new ArgumentNullException(nameof(subscribeLayoutUpdated));
        _unsubscribeLayoutUpdated = unsubscribeLayoutUpdated ?? throw new ArgumentNullException(nameof(unsubscribeLayoutUpdated));
        _subscribeWindowPositionChanged = subscribeWindowPositionChanged ?? throw new ArgumentNullException(nameof(subscribeWindowPositionChanged));
        _unsubscribeWindowPositionChanged = unsubscribeWindowPositionChanged ?? throw new ArgumentNullException(nameof(unsubscribeWindowPositionChanged));
        _refreshOverlayLayout = refreshOverlayLayout ?? throw new ArgumentNullException(nameof(refreshOverlayLayout));
    }

    public WebViewOverlayHost? OverlayHost => _overlayHost;

    public void UpdateOverlayContent(object? content)
    {
        if (content is not null)
        {
            _overlayHost ??= _createOverlayHost();
            _overlayHost.Content = content;

            if (_hasVisualRoot())
                _refreshOverlayLayout();

            return;
        }

        DisposeOverlayHost();
    }

    public void AttachVisualHooks()
    {
        if (_layoutUpdatedHandler is null)
        {
            _layoutUpdatedHandler = (_, _) =>
            {
                RefreshWindowPositionHook();
                _refreshOverlayLayout();
            };

            _subscribeLayoutUpdated(_layoutUpdatedHandler);
        }

        RefreshWindowPositionHook();
    }

    public void DetachVisualHooks()
    {
        if (_layoutUpdatedHandler is not null)
        {
            _unsubscribeLayoutUpdated(_layoutUpdatedHandler);
            _layoutUpdatedHandler = null;
        }

        if (_windowPositionChangedHandler is not null && _positionChangedWindow is not null)
        {
            _unsubscribeWindowPositionChanged(_positionChangedWindow, _windowPositionChangedHandler);
        }

        _windowPositionChangedHandler = null;
        _positionChangedWindow = null;
    }

    public void DisposeOverlayHost()
    {
        _overlayHost?.Dispose();
        _overlayHost = null;
    }

    public void Dispose()
    {
        DetachVisualHooks();
        DisposeOverlayHost();
    }

    private void RefreshWindowPositionHook()
    {
        var window = _getTopLevelWindow();
        if (ReferenceEquals(window, _positionChangedWindow))
            return;

        if (_windowPositionChangedHandler is not null && _positionChangedWindow is not null)
        {
            _unsubscribeWindowPositionChanged(_positionChangedWindow, _windowPositionChangedHandler);
        }

        _positionChangedWindow = window;

        if (window is null)
        {
            _windowPositionChangedHandler = null;
            return;
        }

        _windowPositionChangedHandler ??= (_, _) => _refreshOverlayLayout();
        _subscribeWindowPositionChanged(window, _windowPositionChangedHandler);
    }
}
