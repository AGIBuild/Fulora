using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agibuild.Fulora.Testing;

/// <summary>
/// Test-only builder for <see cref="WebViewCoreContext"/> that lets unit tests construct a runtime
/// dependency container without wiring up a full <see cref="WebViewCore"/>. Runtimes accept the
/// context as their sole collaborator, so tests can assert behaviour against stub adapters /
/// dispatchers without maintaining per-runtime mock host classes.
/// </summary>
internal static class WebViewCoreTestContext
{
    /// <summary>
    /// Creates a context pre-transitioned to the <see cref="WebViewLifecycleState.Ready"/> state so
    /// that operations enqueued against it are admitted by the operation queue. Pass a pre-configured
    /// <paramref name="lifecycle"/> to simulate detaching / disposed scenarios.
    /// </summary>
    public static WebViewCoreContext Create(
        IWebViewAdapter adapter,
        IWebViewDispatcher? dispatcher = null,
        ILogger? logger = null,
        IWebViewEnvironmentOptions? environmentOptions = null,
        WebViewLifecycleStateMachine? lifecycle = null,
        WebViewCoreEventHub? events = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        dispatcher ??= new TestDispatcher();
        logger ??= NullLogger.Instance;
        environmentOptions ??= new WebViewEnvironmentOptions();
        lifecycle ??= CreateReadyLifecycle();
        events ??= new WebViewCoreEventHub(new object());

        var operations = new WebViewCoreOperationQueue(lifecycle, dispatcher, logger);
        var capabilities = AdapterCapabilities.From(adapter);

        return new WebViewCoreContext(
            adapter,
            capabilities,
            dispatcher,
            logger,
            environmentOptions,
            lifecycle,
            events,
            operations,
            Guid.NewGuid());
    }

    /// <summary>Creates a lifecycle machine already in the Ready state.</summary>
    public static WebViewLifecycleStateMachine CreateReadyLifecycle()
    {
        var lifecycle = new WebViewLifecycleStateMachine();
        lifecycle.TransitionToAttaching();
        lifecycle.TransitionToReady();
        return lifecycle;
    }
}
