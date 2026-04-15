using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WebViewControlStateRuntimeTests
{
    [Fact]
    public void HandleSourceChanged_navigates_only_when_core_is_attached()
    {
        Uri? navigatedUri = null;
        var attached = false;
        var runtime = CreateRuntime(
            isCoreAttached: () => attached,
            navigateAsync: uri =>
            {
                navigatedUri = uri;
                return Task.CompletedTask;
            });

        runtime.HandleSourceChanged(new Uri("https://ignored.test"));
        Assert.Null(navigatedUri);

        attached = true;
        runtime.HandleSourceChanged(new Uri("https://example.test"));

        Assert.Equal(new Uri("https://example.test"), navigatedUri);
    }

    [Fact]
    public void HandleZoomFactorChanged_updates_core_only_when_attached()
    {
        double? appliedZoom = null;
        var attached = false;
        var runtime = CreateRuntime(
            isCoreAttached: () => attached,
            setZoomFactorAsync: zoom =>
            {
                appliedZoom = zoom;
                return Task.CompletedTask;
            });

        runtime.HandleZoomFactorChanged(1.25);
        Assert.Null(appliedZoom);

        attached = true;
        runtime.HandleZoomFactorChanged(1.5);

        Assert.Equal(1.5, appliedZoom);
    }

    [Fact]
    public void HandleCoreNavigationStarted_raises_navigation_and_loading_change()
    {
        NavigationStartingEventArgs? raisedArgs = null;
        var loadingChanges = new List<(bool OldValue, bool NewValue)>();
        var runtime = CreateRuntime(
            raiseNavigationStarted: args => raisedArgs = args,
            raiseIsLoadingChanged: (oldValue, newValue) => loadingChanges.Add((oldValue, newValue)));
        var args = new NavigationStartingEventArgs(new Uri("https://example.test"));

        runtime.HandleCoreNavigationStarted(args);

        Assert.Same(args, raisedArgs);
        Assert.Equal([(false, true)], loadingChanges);
    }

    [Fact]
    public void HandleCoreNavigationCompleted_raises_navigation_and_history_state_changes()
    {
        NavigationCompletedEventArgs? raisedArgs = null;
        var loadingChanges = new List<(bool OldValue, bool NewValue)>();
        var backChanges = new List<(bool OldValue, bool NewValue)>();
        var forwardChanges = new List<(bool OldValue, bool NewValue)>();
        var runtime = CreateRuntime(
            raiseNavigationCompleted: args => raisedArgs = args,
            getCanGoBack: () => true,
            getCanGoForward: () => false,
            raiseIsLoadingChanged: (oldValue, newValue) => loadingChanges.Add((oldValue, newValue)),
            raiseCanGoBackChanged: (oldValue, newValue) => backChanges.Add((oldValue, newValue)),
            raiseCanGoForwardChanged: (oldValue, newValue) => forwardChanges.Add((oldValue, newValue)));
        var args = new NavigationCompletedEventArgs(Guid.NewGuid(), new Uri("https://example.test"), NavigationCompletedStatus.Success, null);

        runtime.HandleCoreNavigationCompleted(args);

        Assert.Same(args, raisedArgs);
        Assert.Equal([(true, false)], loadingChanges);
        Assert.Equal([(false, true)], backChanges);
        Assert.Equal([(true, false)], forwardChanges);
    }

    [Fact]
    public void HandleCoreZoomFactorChanged_updates_property_and_event()
    {
        double? currentZoom = null;
        double? raisedZoom = null;
        var runtime = CreateRuntime(
            setZoomFactorValue: zoom => currentZoom = zoom,
            raiseZoomFactorChanged: zoom => raisedZoom = zoom);

        runtime.HandleCoreZoomFactorChanged(1.75);

        Assert.Equal(1.75, currentZoom);
        Assert.Equal(1.75, raisedZoom);
    }

    private static WebViewControlStateRuntime CreateRuntime(
        Func<bool>? isCoreAttached = null,
        Func<Uri, Task>? navigateAsync = null,
        Func<double, Task>? setZoomFactorAsync = null,
        Action<NavigationStartingEventArgs>? raiseNavigationStarted = null,
        Action<NavigationCompletedEventArgs>? raiseNavigationCompleted = null,
        Func<bool>? getCanGoBack = null,
        Func<bool>? getCanGoForward = null,
        Action<bool, bool>? raiseIsLoadingChanged = null,
        Action<bool, bool>? raiseCanGoBackChanged = null,
        Action<bool, bool>? raiseCanGoForwardChanged = null,
        Action<double>? setZoomFactorValue = null,
        Action<double>? raiseZoomFactorChanged = null)
    {
        return new WebViewControlStateRuntime(
            isCoreAttached: isCoreAttached ?? (() => true),
            navigateAsync: navigateAsync ?? (_ => Task.CompletedTask),
            setZoomFactorAsync: setZoomFactorAsync ?? (_ => Task.CompletedTask),
            raiseNavigationStarted: raiseNavigationStarted ?? (_ => { }),
            raiseNavigationCompleted: raiseNavigationCompleted ?? (_ => { }),
            getCanGoBack: getCanGoBack ?? (() => false),
            getCanGoForward: getCanGoForward ?? (() => false),
            raiseIsLoadingChanged: raiseIsLoadingChanged ?? ((_, _) => { }),
            raiseCanGoBackChanged: raiseCanGoBackChanged ?? ((_, _) => { }),
            raiseCanGoForwardChanged: raiseCanGoForwardChanged ?? ((_, _) => { }),
            setZoomFactorValue: setZoomFactorValue ?? (_ => { }),
            raiseZoomFactorChanged: raiseZoomFactorChanged ?? (_ => { }));
    }
}
