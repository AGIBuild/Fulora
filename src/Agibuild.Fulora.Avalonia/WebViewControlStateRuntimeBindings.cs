namespace Agibuild.Fulora;

/// <summary>
/// Groups the Avalonia shell-side output callbacks used by
/// <see cref="WebViewControlStateRuntime"/> to raise events and update
/// property values on the control.
/// </summary>
internal sealed class WebViewControlStateCallbacks(
    Action<NavigationStartingEventArgs> raiseNavigationStarted,
    Action<NavigationCompletedEventArgs> raiseNavigationCompleted,
    Action<bool, bool> raiseIsLoadingChanged,
    Action<bool, bool> raiseCanGoBackChanged,
    Action<bool, bool> raiseCanGoForwardChanged,
    Action<double> setZoomFactorValue,
    Action<double> raiseZoomFactorChanged)
{
    public Action<NavigationStartingEventArgs> RaiseNavigationStarted { get; } = raiseNavigationStarted ?? throw new ArgumentNullException(nameof(raiseNavigationStarted));
    public Action<NavigationCompletedEventArgs> RaiseNavigationCompleted { get; } = raiseNavigationCompleted ?? throw new ArgumentNullException(nameof(raiseNavigationCompleted));
    public Action<bool, bool> RaiseIsLoadingChanged { get; } = raiseIsLoadingChanged ?? throw new ArgumentNullException(nameof(raiseIsLoadingChanged));
    public Action<bool, bool> RaiseCanGoBackChanged { get; } = raiseCanGoBackChanged ?? throw new ArgumentNullException(nameof(raiseCanGoBackChanged));
    public Action<bool, bool> RaiseCanGoForwardChanged { get; } = raiseCanGoForwardChanged ?? throw new ArgumentNullException(nameof(raiseCanGoForwardChanged));
    public Action<double> SetZoomFactorValue { get; } = setZoomFactorValue ?? throw new ArgumentNullException(nameof(setZoomFactorValue));
    public Action<double> RaiseZoomFactorChanged { get; } = raiseZoomFactorChanged ?? throw new ArgumentNullException(nameof(raiseZoomFactorChanged));
}
