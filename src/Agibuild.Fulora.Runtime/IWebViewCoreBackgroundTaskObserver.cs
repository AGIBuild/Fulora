namespace Agibuild.Fulora;

/// <summary>
/// Shared contract for runtime collaborators that need to hand fire-and-forget tasks back to
/// <see cref="WebViewCore"/> so exceptions are surfaced and never lost.
/// </summary>
/// <remarks>
/// Implemented explicitly by <see cref="WebViewCore"/>. Only runtimes that genuinely spawn
/// background work (bridge, feature) consume this base; navigation/adapter-event runtimes run
/// synchronously on the dispatcher and intentionally do not observe it.
/// </remarks>
internal interface IWebViewCoreBackgroundTaskObserver
{
    /// <summary>Observes a background task, surfacing any unobserved exception through the core.</summary>
    /// <param name="task">The task to observe.</param>
    /// <param name="operationType">Human-readable label used when logging faults.</param>
    void ObserveBackgroundTask(Task task, string operationType);
}
