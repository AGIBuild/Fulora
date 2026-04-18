using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal readonly record struct WebViewCoreNavigationState(
    Guid NavigationId,
    Guid CorrelationId,
    Uri RequestUri);

internal interface IWebViewCoreNavigationHost : IWebViewCoreLifecycleHost
{
    void RaiseNavigationStarting(NavigationStartingEventArgs args);

    void RaiseNavigationCompleted(NavigationCompletedEventArgs args);

    void ReinjectBridgeStubsIfEnabled();

    void SetSource(Uri uri);

    void ThrowIfNotOnUiThread(string apiName);
}

/// <summary>
/// Navigation coordinator that owns the single active <see cref="NavigationOperation"/> (if any),
/// marshals navigation start / completion onto the UI thread, and funnels observable events through
/// the <see cref="IWebViewCoreNavigationHost"/> (<see cref="WebViewCore"/>).
/// </summary>
/// <remarks>
/// Intentionally not <see cref="IDisposable"/>: the active-navigation <see cref="TaskCompletionSource"/>
/// stored inside <see cref="NavigationOperation"/> is released either when the adapter raises
/// <c>NavigationCompleted</c> or when the host calls <see cref="FaultActiveForDispose"/> from
/// <see cref="WebViewCore.Dispose"/>. This runtime holds only injected references plus the optional
/// active operation — no unmanaged handles, timers, or background tasks.
/// </remarks>
internal sealed class WebViewCoreNavigationRuntime
{
    private static readonly Uri AboutBlank = new("about:blank");

    private readonly IWebViewCoreNavigationHost _host;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;

    private NavigationOperation? _activeNavigation;

    public WebViewCoreNavigationRuntime(
        IWebViewCoreNavigationHost host,
        IWebViewDispatcher dispatcher,
        ILogger logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Whether a navigation is currently in flight.
    /// </summary>
    public bool IsLoading => _activeNavigation is not null;

    /// <summary>
    /// Creates a new active navigation, overwriting any previous one (callers are expected to have
    /// already handled supersession / cancellation of the prior navigation).
    /// </summary>
    public Task SetActiveNavigation(Guid navigationId, Guid correlationId, Uri requestUri)
    {
        var operation = new NavigationOperation(navigationId, correlationId, requestUri);
        _activeNavigation = operation;
        return operation.Task;
    }

    public bool TryGetActiveNavigation(out WebViewCoreNavigationState state)
    {
        if (_activeNavigation is { } operation)
        {
            state = new WebViewCoreNavigationState(operation.NavigationId, operation.CorrelationId, operation.RequestUri);
            return true;
        }

        state = default;
        return false;
    }

    public void UpdateActiveNavigationRequestUri(Uri requestUri)
        => _activeNavigation?.UpdateRequestUri(requestUri);

    /// <summary>
    /// Cancels the active navigation, if any, and returns whether a navigation was actually stopped.
    /// </summary>
    public bool TryStopActiveNavigation()
    {
        if (_activeNavigation is null)
        {
            return false;
        }

        _logger.LogDebug("Stop: canceling active navigation id={NavigationId}", _activeNavigation.NavigationId);
        CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
        return true;
    }

    /// <summary>
    /// Faults the active navigation (if any) with the given exception without raising any events.
    /// Used by <see cref="WebViewCore.Dispose"/>, where observers are no longer welcome.
    /// </summary>
    public void FaultActiveForDispose(Exception exception)
    {
        var operation = _activeNavigation;
        if (operation is null)
        {
            return;
        }

        _logger.LogDebug("Dispose: faulting active navigation id={NavigationId}", operation.NavigationId);
        operation.TrySetFault(exception);
        _activeNavigation = null;
    }

    /// <summary>
    /// Completes the active navigation with the given status, raises <c>NavigationCompleted</c>,
    /// and resolves / faults the corresponding <see cref="NavigationOperation"/> task.
    /// Idempotent when there is no active navigation.
    /// </summary>
    public void CompleteActiveNavigation(NavigationCompletedStatus status, Exception? error)
    {
        var operation = _activeNavigation;
        if (operation is null)
        {
            return;
        }

        _activeNavigation = null;

        _logger.LogDebug("Event NavigationCompleted: id={NavigationId}, status={Status}, uri={Uri}, error={Error}",
            operation.NavigationId, status, operation.RequestUri, error?.Message);

        NavigationCompletedEventArgs completedArgs;
        try
        {
            completedArgs = new NavigationCompletedEventArgs(
                operation.NavigationId,
                operation.RequestUri,
                status,
                status == NavigationCompletedStatus.Failure ? error : null);
        }
        catch (Exception ex)
        {
            completedArgs = new NavigationCompletedEventArgs(operation.NavigationId, operation.RequestUri, NavigationCompletedStatus.Failure, ex);
            status = NavigationCompletedStatus.Failure;
            error = ex;
        }

        _host.RaiseNavigationCompleted(completedArgs);

        if (status == NavigationCompletedStatus.Failure)
        {
            // Preserve categorized exception subclasses from the adapter (Network, SSL, Timeout).
            var faultException = error is WebViewNavigationException navEx
                ? navEx
                : new WebViewNavigationException(
                    message: "Navigation failed.",
                    navigationId: operation.NavigationId,
                    requestUri: operation.RequestUri,
                    innerException: error);
            operation.TrySetFault(faultException);
        }
        else
        {
            if (status == NavigationCompletedStatus.Success)
                _host.ReinjectBridgeStubsIfEnabled();

            operation.TrySetSuccess();
        }
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
        _ = SetActiveNavigation(navigationId, info.CorrelationId, requestUri);

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (native): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
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

        if (!TryGetActiveNavigation(out var activeNavigation))
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

        UpdateActiveNavigationRequestUri(args.RequestUri);
        CompleteActiveNavigation(status, error);
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

        if (TryGetActiveNavigation(out var activeNavigation))
        {
            _logger.LogDebug("StartNavigation: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        var operationTask = SetActiveNavigation(navigationId, navigationId, requestUri);

        var startingArgs = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (API): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(startingArgs);

        if (startingArgs.Cancel)
        {
            _logger.LogDebug("StartNavigation: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return operationTask;
        }

        await AwaitNavigationCompletion(navigationId, adapterInvoke).ConfigureAwait(false);
        return operationTask;
    }

    public Guid StartCommandNavigation(Uri requestUri)
    {
        if (TryGetActiveNavigation(out var activeNavigation))
        {
            _logger.LogDebug("StartCommandNavigation: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
        }

        var navigationId = Guid.NewGuid();
        _ = SetActiveNavigation(navigationId, navigationId, requestUri);

        var args = new NavigationStartingEventArgs(navigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (command): id={NavigationId}, uri={Uri}", navigationId, requestUri);
        _host.RaiseNavigationStarting(args);

        if (args.Cancel)
        {
            _logger.LogDebug("StartCommandNavigation: canceled by handler, id={NavigationId}", navigationId);
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            return Guid.Empty;
        }

        return navigationId;
    }

    private bool TryHandleNavigationRedirect(
        NativeNavigationStartingInfo info,
        Uri requestUri,
        out NativeNavigationStartingDecision decision)
    {
        if (!TryGetActiveNavigation(out var activeNavigation) || activeNavigation.CorrelationId != info.CorrelationId)
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

        UpdateActiveNavigationRequestUri(requestUri);
        _host.SetSource(requestUri);

        var redirectArgs = new NavigationStartingEventArgs(activeNavigation.NavigationId, requestUri);
        _logger.LogDebug("Event NavigationStarted (redirect): id={NavigationId}, uri={Uri}", activeNavigation.NavigationId, requestUri);
        _host.RaiseNavigationStarting(redirectArgs);

        if (redirectArgs.Cancel)
        {
            _logger.LogDebug("OnNativeNavigationStarting: redirect canceled by handler, id={NavigationId}", activeNavigation.NavigationId);
            var activeNavigationId = activeNavigation.NavigationId;
            CompleteActiveNavigation(NavigationCompletedStatus.Canceled, error: null);
            decision = new NativeNavigationStartingDecision(IsAllowed: false, NavigationId: activeNavigationId);
            return true;
        }

        decision = new NativeNavigationStartingDecision(IsAllowed: true, NavigationId: activeNavigation.NavigationId);
        return true;
    }

    private void HandleNavigationSupersession()
    {
        if (!TryGetActiveNavigation(out var activeNavigation))
        {
            return;
        }

        _logger.LogDebug("OnNativeNavigationStarting: superseding active navigation id={NavigationId}", activeNavigation.NavigationId);
        CompleteActiveNavigation(NavigationCompletedStatus.Superseded, error: null);
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
            CompleteActiveNavigation(NavigationCompletedStatus.Failure, ex);
        }
    }

    private sealed class NavigationOperation
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public NavigationOperation(Guid navigationId, Guid correlationId, Uri requestUri)
        {
            NavigationId = navigationId;
            CorrelationId = correlationId;
            RequestUri = requestUri;
        }

        public Guid NavigationId { get; }
        public Guid CorrelationId { get; }
        public Uri RequestUri { get; private set; }

        public Task Task => _tcs.Task;

        public void UpdateRequestUri(Uri requestUri) => RequestUri = requestUri;

        public void TrySetSuccess() => _tcs.TrySetResult();

        public void TrySetFault(Exception ex) => _tcs.TrySetException(ex);
    }
}
