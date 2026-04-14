using Avalonia.Controls;

namespace Agibuild.Fulora;

internal sealed class WebViewHostClosingRuntime
{
    private readonly Func<object?> _resolveHostWindow;
    private readonly Func<object, Action<bool, WindowCloseReason>, object?> _subscribe;
    private readonly Action<object, object?> _unsubscribe;
    private readonly Func<bool> _isCoreAttached;
    private readonly Func<bool, WindowCloseReason, bool> _detachForClosing;

    private object? _hostWindow;
    private object? _subscriptionToken;
    private Action<bool, WindowCloseReason>? _hostWindowClosingHandler;

    public WebViewHostClosingRuntime(
        Func<object?> resolveHostWindow,
        Func<object, Action<bool, WindowCloseReason>, object?> subscribe,
        Action<object, object?> unsubscribe,
        Func<bool> isCoreAttached,
        Func<bool, WindowCloseReason, bool> detachForClosing)
    {
        _resolveHostWindow = resolveHostWindow ?? throw new ArgumentNullException(nameof(resolveHostWindow));
        _subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        _isCoreAttached = isCoreAttached ?? throw new ArgumentNullException(nameof(isCoreAttached));
        _detachForClosing = detachForClosing ?? throw new ArgumentNullException(nameof(detachForClosing));
    }

    public void RefreshHook()
    {
        var window = _resolveHostWindow();
        if (ReferenceEquals(window, _hostWindow))
            return;

        Unhook();

        if (window is null)
            return;

        _hostWindow = window;
        _hostWindowClosingHandler = (isProgrammatic, closeReason) =>
        {
            _ = HandleHostWindowClosing(isProgrammatic, closeReason);
        };

        _subscriptionToken = _subscribe(window, _hostWindowClosingHandler);
    }

    public void Unhook()
    {
        if (_hostWindow is not null && _hostWindowClosingHandler is not null)
            _unsubscribe(_hostWindow, _subscriptionToken);

        _hostWindow = null;
        _subscriptionToken = null;
        _hostWindowClosingHandler = null;
    }

    public bool HandleHostWindowClosing(bool isProgrammatic, WindowCloseReason closeReason)
    {
        if (!ShouldDetachForHostWindowClosing(isProgrammatic, closeReason))
            return false;

        if (!_isCoreAttached())
            return false;

        try
        {
            return _detachForClosing(isProgrammatic, closeReason);
        }
        catch
        {
            return false;
        }
    }

    internal static bool ShouldDetachForHostWindowClosing(bool isProgrammatic, WindowCloseReason closeReason)
        => true;
}
