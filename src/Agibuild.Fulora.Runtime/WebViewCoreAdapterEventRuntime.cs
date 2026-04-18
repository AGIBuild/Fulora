using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Stateless translator that forwards adapter-originated events onto the dispatcher and into the
/// shared <see cref="WebViewCoreEventHub"/>.
/// </summary>
/// <remarks>
/// Intentionally not <see cref="IDisposable"/>: subscription to the underlying platform adapter
/// events is owned by <see cref="WebViewCoreEventWiringRuntime"/>, which is disposed by
/// <see cref="WebViewCore.Dispose"/>. This runtime holds no timers, handles, or subscriptions of
/// its own; every call is a pass-through into <see cref="WebViewCoreContext.Events"/> (or the
/// injected navigate callback for unhandled new-window requests).
/// </remarks>
internal sealed class WebViewCoreAdapterEventRuntime
{
    private readonly WebViewCoreContext _context;
    private readonly Func<Uri, Task> _navigateAsync;

    public WebViewCoreAdapterEventRuntime(
        WebViewCoreContext context,
        Func<Uri, Task> navigateAsync)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _navigateAsync = navigateAsync ?? throw new ArgumentNullException(nameof(navigateAsync));
    }

    public void HandleAdapterNewWindowRequested(NewWindowRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event NewWindowRequested: uri={Uri}", args.Uri);

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => HandleAdapterNewWindowRequestedOnUiThread(args),
            _context.Logger,
            "NewWindowRequested: ignored (disposed or destroyed)");
    }

    public void HandleAdapterWebResourceRequested(WebResourceRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event WebResourceRequested");

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => _context.Events.RaiseWebResourceRequested(args));
    }

    public void HandleAdapterEnvironmentRequested(EnvironmentRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event EnvironmentRequested");

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => _context.Events.RaiseEnvironmentRequested(args));
    }

    public void HandleAdapterDownloadRequested(DownloadRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event DownloadRequested: uri={Uri}, file={File}", args.DownloadUri, args.SuggestedFileName);

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => _context.Events.RaiseDownloadRequested(args));
    }

    public void HandleAdapterPermissionRequested(PermissionRequestedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _context.Logger.LogDebug("Event PermissionRequested: kind={Kind}, origin={Origin}", args.PermissionKind, args.Origin);

        UiThreadHelper.SafeDispatch(
            _context.Dispatcher,
            _context.Lifecycle.IsDisposed,
            _context.Lifecycle.IsAdapterDestroyed,
            () => _context.Events.RaisePermissionRequested(args));
    }

    private void HandleAdapterNewWindowRequestedOnUiThread(NewWindowRequestedEventArgs args)
    {
        if (_context.Lifecycle.IsDisposed)
        {
            return;
        }

        _context.Events.RaiseNewWindowRequested(args);

        if (!args.Handled && args.Uri is not null)
        {
            _context.Logger.LogDebug("NewWindowRequested: unhandled, navigating in-view to {Uri}", args.Uri);
            _ = _navigateAsync(args.Uri);
        }
    }
}
