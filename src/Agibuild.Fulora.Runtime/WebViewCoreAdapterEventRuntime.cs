using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal interface IWebViewCoreAdapterEventHost
{
    bool IsDisposed { get; }

    bool IsAdapterDestroyed { get; }

    Task NavigateAsync(Uri uri);

    void RaiseNewWindowRequested(NewWindowRequestedEventArgs args);

    void RaiseWebResourceRequested(WebResourceRequestedEventArgs args);

    void RaiseEnvironmentRequested(EnvironmentRequestedEventArgs args);

    void RaiseDownloadRequested(DownloadRequestedEventArgs args);

    void RaisePermissionRequested(PermissionRequestedEventArgs args);
}

internal sealed class WebViewCoreAdapterEventRuntime
{
    private readonly IWebViewCoreAdapterEventHost _host;
    private readonly IWebViewDispatcher _dispatcher;
    private readonly ILogger _logger;

    public WebViewCoreAdapterEventRuntime(
        IWebViewCoreAdapterEventHost host,
        IWebViewDispatcher dispatcher,
        ILogger logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void HandleAdapterNewWindowRequested(NewWindowRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Event NewWindowRequested: uri={Uri}", args.Uri);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => HandleAdapterNewWindowRequestedOnUiThread(args),
            _logger,
            "NewWindowRequested: ignored (disposed or destroyed)");
    }

    public void HandleAdapterWebResourceRequested(WebResourceRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Event WebResourceRequested");

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => _host.RaiseWebResourceRequested(args));
    }

    public void HandleAdapterEnvironmentRequested(EnvironmentRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Event EnvironmentRequested");

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => _host.RaiseEnvironmentRequested(args));
    }

    public void HandleAdapterDownloadRequested(DownloadRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Event DownloadRequested: uri={Uri}, file={File}", args.DownloadUri, args.SuggestedFileName);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => _host.RaiseDownloadRequested(args));
    }

    public void HandleAdapterPermissionRequested(PermissionRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _logger.LogDebug("Event PermissionRequested: kind={Kind}, origin={Origin}", args.PermissionKind, args.Origin);

        UiThreadHelper.SafeDispatch(
            _dispatcher,
            _host.IsDisposed,
            _host.IsAdapterDestroyed,
            () => _host.RaisePermissionRequested(args));
    }

    private void HandleAdapterNewWindowRequestedOnUiThread(NewWindowRequestedEventArgs args)
    {
        if (_host.IsDisposed)
        {
            return;
        }

        _host.RaiseNewWindowRequested(args);

        if (!args.Handled && args.Uri is not null)
        {
            _logger.LogDebug("NewWindowRequested: unhandled, navigating in-view to {Uri}", args.Uri);
            _ = _host.NavigateAsync(args.Uri);
        }
    }
}
