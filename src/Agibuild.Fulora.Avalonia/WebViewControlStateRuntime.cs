namespace Agibuild.Fulora;

internal sealed class WebViewControlStateRuntime
{
    private readonly Func<bool> _isCoreAttached;
    private readonly Func<Uri, Task> _navigateAsync;
    private readonly Func<double, Task> _setZoomFactorAsync;
    private readonly Action<NavigationStartingEventArgs> _raiseNavigationStarted;
    private readonly Action<NavigationCompletedEventArgs> _raiseNavigationCompleted;
    private readonly Func<bool> _getCanGoBack;
    private readonly Func<bool> _getCanGoForward;
    private readonly Action<bool, bool> _raiseIsLoadingChanged;
    private readonly Action<bool, bool> _raiseCanGoBackChanged;
    private readonly Action<bool, bool> _raiseCanGoForwardChanged;
    private readonly Action<double> _setZoomFactorValue;
    private readonly Action<double> _raiseZoomFactorChanged;

    public WebViewControlStateRuntime(
        Func<bool> isCoreAttached,
        Func<Uri, Task> navigateAsync,
        Func<double, Task> setZoomFactorAsync,
        Action<NavigationStartingEventArgs> raiseNavigationStarted,
        Action<NavigationCompletedEventArgs> raiseNavigationCompleted,
        Func<bool> getCanGoBack,
        Func<bool> getCanGoForward,
        Action<bool, bool> raiseIsLoadingChanged,
        Action<bool, bool> raiseCanGoBackChanged,
        Action<bool, bool> raiseCanGoForwardChanged,
        Action<double> setZoomFactorValue,
        Action<double> raiseZoomFactorChanged)
    {
        _isCoreAttached = isCoreAttached ?? throw new ArgumentNullException(nameof(isCoreAttached));
        _navigateAsync = navigateAsync ?? throw new ArgumentNullException(nameof(navigateAsync));
        _setZoomFactorAsync = setZoomFactorAsync ?? throw new ArgumentNullException(nameof(setZoomFactorAsync));
        _raiseNavigationStarted = raiseNavigationStarted ?? throw new ArgumentNullException(nameof(raiseNavigationStarted));
        _raiseNavigationCompleted = raiseNavigationCompleted ?? throw new ArgumentNullException(nameof(raiseNavigationCompleted));
        _getCanGoBack = getCanGoBack ?? throw new ArgumentNullException(nameof(getCanGoBack));
        _getCanGoForward = getCanGoForward ?? throw new ArgumentNullException(nameof(getCanGoForward));
        _raiseIsLoadingChanged = raiseIsLoadingChanged ?? throw new ArgumentNullException(nameof(raiseIsLoadingChanged));
        _raiseCanGoBackChanged = raiseCanGoBackChanged ?? throw new ArgumentNullException(nameof(raiseCanGoBackChanged));
        _raiseCanGoForwardChanged = raiseCanGoForwardChanged ?? throw new ArgumentNullException(nameof(raiseCanGoForwardChanged));
        _setZoomFactorValue = setZoomFactorValue ?? throw new ArgumentNullException(nameof(setZoomFactorValue));
        _raiseZoomFactorChanged = raiseZoomFactorChanged ?? throw new ArgumentNullException(nameof(raiseZoomFactorChanged));
    }

    public void HandleSourceChanged(object? newValue)
    {
        if (!_isCoreAttached() || newValue is not Uri newUri)
            return;

        _ = _navigateAsync(newUri);
    }

    public void HandleZoomFactorChanged(object? newValue)
    {
        if (!_isCoreAttached() || newValue is not double newZoom)
            return;

        _ = _setZoomFactorAsync(newZoom);
    }

    public void HandleCoreNavigationStarted(NavigationStartingEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _raiseNavigationStarted(args);
        _raiseIsLoadingChanged(false, true);
    }

    public void HandleCoreNavigationCompleted(NavigationCompletedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _raiseNavigationCompleted(args);
        _raiseIsLoadingChanged(true, false);

        var canGoBack = _getCanGoBack();
        var canGoForward = _getCanGoForward();
        _raiseCanGoBackChanged(!canGoBack, canGoBack);
        _raiseCanGoForwardChanged(!canGoForward, canGoForward);
    }

    public void HandleCoreZoomFactorChanged(double newZoom)
    {
        _setZoomFactorValue(newZoom);
        _raiseZoomFactorChanged(newZoom);
    }
}
