using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Shared helpers for UI-thread dispatch in adapter event handlers.
/// </summary>
internal static class UiThreadHelper
{
    /// <summary>
    /// Safely dispatches an action to the UI thread if the host is not disposed or destroyed.
    /// If already on the UI thread, runs the action directly; otherwise invokes via the dispatcher.
    /// </summary>
    /// <param name="dispatcher">The UI thread dispatcher.</param>
    /// <param name="disposed">Whether the host has been disposed.</param>
    /// <param name="adapterDestroyed">Whether the adapter has been destroyed.</param>
    /// <param name="action">The action to run on the UI thread.</param>
    /// <param name="logger">Optional logger for ignored-events diagnostics.</param>
    /// <param name="logMessageWhenIgnored">When set and the call is ignored (disposed/destroyed), logs this message.</param>
    public static void SafeDispatch(
        IWebViewDispatcher dispatcher,
        bool disposed,
        bool adapterDestroyed,
        Action action,
        ILogger? logger = null,
        string? logMessageWhenIgnored = null)
    {
        if (disposed || adapterDestroyed)
        {
            if (logger is not null && logMessageWhenIgnored is not null)
            {
                logger.LogDebug(logMessageWhenIgnored);
            }
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }
}
