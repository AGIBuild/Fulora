namespace Agibuild.Fulora;

internal sealed class WebViewControlStateRuntime
{
    private readonly Func<bool> _isCoreAttached;
    private readonly Func<Uri, Task> _navigateAsync;
    private readonly Func<double, Task> _setZoomFactorAsync;
    private readonly Func<bool> _getCanGoBack;
    private readonly Func<bool> _getCanGoForward;
    private readonly WebViewControlStateCallbacks _shellCallbacks;

    public WebViewControlStateRuntime(
        Func<bool> isCoreAttached,
        Func<Uri, Task> navigateAsync,
        Func<double, Task> setZoomFactorAsync,
        Func<bool> getCanGoBack,
        Func<bool> getCanGoForward,
        WebViewControlStateCallbacks shellCallbacks)
    {
        _isCoreAttached = isCoreAttached ?? throw new ArgumentNullException(nameof(isCoreAttached));
        _navigateAsync = navigateAsync ?? throw new ArgumentNullException(nameof(navigateAsync));
        _setZoomFactorAsync = setZoomFactorAsync ?? throw new ArgumentNullException(nameof(setZoomFactorAsync));
        _getCanGoBack = getCanGoBack ?? throw new ArgumentNullException(nameof(getCanGoBack));
        _getCanGoForward = getCanGoForward ?? throw new ArgumentNullException(nameof(getCanGoForward));
        _shellCallbacks = shellCallbacks ?? throw new ArgumentNullException(nameof(shellCallbacks));
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

        _shellCallbacks.RaiseNavigationStarted(args);
        _shellCallbacks.RaiseIsLoadingChanged(false, true);
    }

    public void HandleCoreNavigationCompleted(NavigationCompletedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        _shellCallbacks.RaiseNavigationCompleted(args);
        _shellCallbacks.RaiseIsLoadingChanged(true, false);

        var canGoBack = _getCanGoBack();
        var canGoForward = _getCanGoForward();
        _shellCallbacks.RaiseCanGoBackChanged(!canGoBack, canGoBack);
        _shellCallbacks.RaiseCanGoForwardChanged(!canGoForward, canGoForward);
    }

    public void HandleCoreZoomFactorChanged(double newZoom)
    {
        _shellCallbacks.SetZoomFactorValue(newZoom);
        _shellCallbacks.RaiseZoomFactorChanged(newZoom);
    }
}
