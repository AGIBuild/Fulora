using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal readonly record struct WebViewCoreNavigationState(
    Guid NavigationId,
    Guid CorrelationId,
    Uri RequestUri);

internal interface IWebViewCoreNavigationHost
{
    bool IsDisposed { get; }

    bool IsAdapterDestroyed { get; }

    void CompleteActiveNavigation(NavigationCompletedStatus status, Exception? error);

    void RaiseNavigationStarting(NavigationStartingEventArgs args);

    Task SetActiveNavigation(Guid navigationId, Guid correlationId, Uri requestUri);

    void SetSource(Uri uri);

    void ThrowIfNotOnUiThread(string apiName);

    bool TryGetActiveNavigation(out WebViewCoreNavigationState state);

    void UpdateActiveNavigationRequestUri(Uri requestUri);
}

internal sealed class WebViewCoreNavigationRuntime
{
    private static readonly Uri AboutBlank = new("about:blank");

    private readonly IWebViewCoreNavigationHost _host;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;

    public WebViewCoreNavigationRuntime(
        IWebViewCoreNavigationHost host,
        IWebViewDispatcher dispatcher,
        ILogger logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<NativeNavigationStartingDecision> OnNativeNavigationStartingAsync(NativeNavigationStartingInfo info)
    {
        _logger.LogDebug("OnNativeNavigationStarting: correlationId={CorrelationId}, uri={Uri}, isMainFrame={IsMainFrame}",
            info.CorrelationId, info.RequestUri, info.IsMainFrame);

        if (_host.IsDisposed)
        {
            _logger.LogDebug("OnNativeNavigationStarting: disposed, denying");
            return ValueTask.FromResult(new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: Guid.Empty));
        }

        if (_dispatcher.CheckAccess())
        {
            return ValueTask.FromResult(OnNativeNavigationStartingOnUiThread(info));
        }

        return new ValueTask<NativeNavigationStartingDecision>(
            _dispatcher.InvokeAsync(() => OnNativeNavigationStartingOnUiThread(info)));
    }

    public NativeNavigationStartingDecision OnNativeNavigationStartingOnUiThread(NativeNavigationStartingInfo info)
    {
        if (_host.IsDisposed)
        {
            return new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: Guid.Empty);
        }

        _host.ThrowIfNotOnUiThread(nameof(IWebViewAdapterHost.OnNativeNavigationStartingAsync));

        if (!info.IsMainFrame)
        {
            _logger.LogDebug("OnNativeNavigationStarting: sub-frame, auto-allow");
            return new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: Guid.Empty);
        }

        var requestUri = info.RequestUri.AbsoluteUri != AboutBlank.AbsoluteUri ? info.RequestUri : AboutBlank;

        if (TryHandleNavigationRedirect(info, requestUri, out var redirectDecision))
        {
            return redirectDecision;
        }

        HandleNavigationSupersession();

        _host.SetSource(requestUri);

        var navigationId = Guid.NewGuid();
        _ = _host.SetActiveNavigation(navigationId, info.CorrelationId, requestUri);

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (native): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: canceled by handler, id={NavigationId}", navigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: navigationId);
        }

        _logger.LogDebug("OnNativeNavigationStarting: allowed, id={NavigationId}", navigationId);
        return new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: navigationId);
    }

    public void HandleAdapterNavigationCompleted(NavigationCompletedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Adapter.NavigationCompleted received: id={NavigationId}, status={Status}, uri={Uri}",
            args.NavigationId, args.Status, args.RequestUri);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => HandleAdapterNavigationCompletedOnUiThread(args),
            _logger,
            "Adapter.NavigationCompleted: ignored (disposed or destroyed)");
    }

    public void HandleAdapterNavigationCompletedOnUiThread(NavigationCompletedEventArgs args)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        if (!_host.TryGetActiveNavigation(out var activeNavigation))
        {
            _logger.LogDebug("Adapter.NavigationCompleted: no active navigation, ignoring id={NavigationId}", args.NavigationId);
            return;
        }

        if (args.NavigationId != activeNavigation.NavigationId)
        {
            _logger.LogDebug("Adapter.NavigationCompleted: id mismatch (received={Received}, active={Active}), ignoring",
                args.NavigationId, activeNavigation.NavigationId);
            return;
        }

        var status = args.Status;
        var error = args.Error;

        if (status == NavigationCompletedStatus.Failure && error is null)
        {
            error = new InvalidOperationException("Navigation failed.");
        }

        _host.UpdateActiveNavigationRequestUri(args.RequestUri);
        _host.CompleteActiveNavigation(status, error);
    }

    public Task StartNavigationCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke)
        => StartNavigationCoreAsync(requestUri, adapterInvoke, updateSource: true);

    public async Task StartNavigationCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
    {
        var completionTask = await StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource).ConfigureAwait(false);
        await completionTask.ConfigureAwait(false);
    }

    public Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke)
        => StartNavigationRequestCoreAsync(requestUri, adapterInvoke, updateSource: true);

    public async Task<Task> StartNavigationRequestCoreAsync(Uri requestUri, Func<Guid, Task> adapterInvoke, bool updateSource)
    {
        ObjectDisposedException.ThrowIf(_host.IsDisposed, nameof(WebViewCore));
        _host.ThrowIfNotOnUiThread("async navigation");

        if (updateSource)
        {
            _host.SetSource(requestUri.AbsoluteUri != AboutBlank.AbsoluteUri ? requestUri : AboutBlank);
        }

        if (_host.TryGetActiveNavigation(out var activeNavigation))
        {
            _logger.LogDebug("StartNavigation: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        var operationTask = _host.SetActiveNavigation(navigationId, navigationId, requestUri);

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (API): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("StartNavigation: canceled by handler, id={NavigationId}", navigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return operationTask;
        }

        await AwaitNavigationCompletion(navigationId, adapterInvoke).ConfigureAwait(false);
        return operationTask;
    }

    public Guid StartCommandNavigation(Uri requestUri)
    {
        if (_host.TryGetActiveNavigation(out var activeNavigation))
        {
            _logger.LogDebug("StartCommandNavigation: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        _ = _host.SetActiveNavigation(navigationId, navigationId, requestUri);

        var args = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (command): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(args);

        if (args.Cancel)
        {
            _logger.LogDebug("StartCommandNavigation: canceled by handler, id={NavigationId}", navigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return Guid.Empty;
        }

        return navigationId;
    }

    private bool TryHandleNavigationRedirect(
        NativeNavigationStartingInfo info,
        Uri requestUri,
        out NativeNavigationStartingDecision decision)
    {
        if (!_host.TryGetActiveNavigation(out var activeNavigation) || activeNavigation.CorrelationId != info.CorrelationId)
        {
            decision = default;
            return false;
        }

        if (activeNavigation.RequestUri.AbsoluteUri == requestUri.AbsoluteUri)
        {
            _logger.LogDebug("OnNativeNavigationStarting: same-URL redirect, id={NavigationId}", activeNavigation.NavigationId);
            decision = new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: activeNavigation.NavigationId);
            return true;
        }

        _host.UpdateActiveNavigationRequestUri(requestUri);
        _host.SetSource(requestUri);

        var redirectArgs = new NavigationStartingEventArgs(activeNavigation.NavigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (redirect): id={NavigationId}, uri={Uri}", activeNavigation.NavigationId, requestUri);
        _host.RaiseNavigationStarting(redirectArgs);

        if (redirectArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: redirect canceled by handler, id={NavigationId}", activeNavigation.NavigationId);
            var activeNavigationId = activeNavigation.NavigationId;
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            decision = new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: activeNavigationId);
            return true;
        }

        decision = new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: activeNavigation.NavigationId);
        return true;
    }

    private void HandleNavigationSupersession()
    {
        if (!_host.TryGetActiveNavigation(out var activeNavigation))
        {
            return;
        }

        _logger.LogDebug("OnNativeNavigationStarting: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
        _host.CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
    }

    private async Task AwaitNavigationCompletion(Guid navigationId, Func<Guid, Task> adapterInvoke)
    {
        try
        {
            await adapterInvoke(navigationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "StartNavigation: adapter invocation failed, id={NavigationId}", navigationId);
            _host.CompleteActiveNavigation(NavigationCompletedStatus.Failure, ex);
        }
    }
}
