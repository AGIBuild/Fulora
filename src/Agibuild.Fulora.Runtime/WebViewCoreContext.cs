using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Shared composition-root container that every <c>WebViewCore*Runtime</c> consumes. Replaces the
/// former per-runtime <c>IWebViewCore*Host</c> interfaces: each runtime now takes a single
/// <see cref="WebViewCoreContext"/> and reaches state observers (lifecycle, dispatcher, logger) and
/// event sinks (<see cref="Events"/>) through explicit properties rather than through a
/// host-specific callback surface.
/// </summary>
/// <remarks>
/// <para>
/// The context is fully populated during <see cref="WebViewCore"/> construction and stays immutable
/// for the lifetime of the core. Runtimes must never mutate the properties — they observe
/// <see cref="Lifecycle"/> (for disposal / adapter-destruction), raise via <see cref="Events"/>,
/// enqueue UI-thread work via <see cref="Operations"/>, and call capability members directly on
/// <see cref="Adapter"/>.
/// </para>
/// <para>
/// Helper methods (<see cref="ThrowIfDisposed"/>, <see cref="ThrowIfNotOnUiThread(string)"/>,
/// <see cref="ObserveBackgroundTask"/>) centralize patterns that used to live as boilerplate on
/// every runtime, so each runtime body reads as the actual domain operation rather than as repeated
/// guard prologues.
/// </para>
/// </remarks>
internal sealed class WebViewCoreContext
{
    private readonly WebViewCoreOperationQueue _operations;

    public WebViewCoreContext(
        IWebViewAdapter adapter,
        AdapterCapabilities capabilities,
        IWebViewDispatcher dispatcher,
        ILogger logger,
        IWebViewEnvironmentOptions environmentOptions,
        WebViewLifecycleStateMachine lifecycle,
        WebViewCoreEventHub events,
        WebViewCoreOperationQueue operations,
        Guid channelId)
    {
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        Capabilities = capabilities;
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        EnvironmentOptions = environmentOptions ?? throw new ArgumentNullException(nameof(environmentOptions));
        Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        ChannelId = channelId;
    }

    public IWebViewAdapter Adapter { get; }

    public AdapterCapabilities Capabilities { get; }

    public IWebViewDispatcher Dispatcher { get; }

    public ILogger Logger { get; }

    public IWebViewEnvironmentOptions EnvironmentOptions { get; }

    public WebViewLifecycleStateMachine Lifecycle { get; }

    public WebViewCoreEventHub Events { get; }

    public WebViewCoreOperationQueue Operations => _operations;

    public Guid ChannelId { get; }

    /// <summary>
    /// Forwards to <see cref="WebViewLifecycleStateMachine.ThrowIfDisposed"/>. Kept here so runtimes
    /// can write <c>_context.ThrowIfDisposed()</c> without pulling in the state machine explicitly.
    /// </summary>
    public void ThrowIfDisposed() => Lifecycle.ThrowIfDisposed();

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when called from a non-UI thread. Hoisted
    /// onto the context so every runtime uses the same enforcement predicate rather than copying
    /// the <c>Dispatcher.CheckAccess()</c> check.
    /// </summary>
    public void ThrowIfNotOnUiThread(string apiName)
    {
        if (!Dispatcher.CheckAccess())
        {
            throw new InvalidOperationException($"'{apiName}' must be called on the UI thread.");
        }
    }

    /// <summary>
    /// Fire-and-forget observation helper: logs but never propagates exceptions from background
    /// tasks (for example, <see cref="WebViewRpcService.JsStub"/> injection).
    /// </summary>
    public void ObserveBackgroundTask(Task task, string operationType)
    {
        ArgumentNullException.ThrowIfNull(task);

        task.ContinueWith(
            t =>
            {
                if (t.Exception is null)
                {
                    return;
                }

                var error = t.Exception.InnerException ?? t.Exception;
                Logger.LogDebug(error, "Background operation faulted: {OperationType}", operationType);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
