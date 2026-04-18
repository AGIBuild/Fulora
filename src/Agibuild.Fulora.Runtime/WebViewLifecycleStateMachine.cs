namespace Agibuild.Fulora;

/// <summary>
/// Collapses the three interleaved lifecycle flags previously held directly on
/// <see cref="WebViewCore"/> — "is disposed", "has the adapter-destroyed event already fired", and
/// the named <see cref="WebViewLifecycleState"/> transition marker — into a single owner with named
/// transition methods and a single admission predicate.
/// </summary>
/// <remarks>
/// <para>
/// Callers interact with the state machine exclusively through named mutators
/// (<see cref="TransitionToAttaching"/>, <see cref="TransitionToReady"/>,
/// <see cref="TransitionToDetaching"/>, <see cref="TryTransitionToDisposed"/>,
/// <see cref="MarkAdapterDestroyedOnce(System.Action)"/>). They must not branch on
/// <see cref="CurrentState"/>; that property exists only for diagnostics. This keeps the admission
/// rule (<see cref="IsOperationAccepted"/>) as a single source of truth — the same rule that
/// <see cref="WebViewCoreOperationQueue"/> consults when deciding whether to admit new work.
/// </para>
/// <para>
/// The two <see langword="volatile"/> fields (<c>_disposed</c> and <c>_state</c>) are read off the UI
/// thread by adapter callbacks that pre-check disposal before dispatching; the non-volatile
/// <c>_adapterDestroyed</c> flag is only touched on the UI thread, mirroring the previous invariant
/// documented on <see cref="WebViewCore"/>.
/// </para>
/// </remarks>
internal sealed class WebViewLifecycleStateMachine
{
    private volatile bool _disposed;
    private bool _adapterDestroyed;
    private volatile WebViewLifecycleState _state = WebViewLifecycleState.Created;

    /// <summary>Gets a value indicating whether <see cref="TryTransitionToDisposed"/> has succeeded.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether <see cref="MarkAdapterDestroyedOnce(System.Action)"/> has run.</summary>
    public bool IsAdapterDestroyed => _adapterDestroyed;

    /// <summary>Gets the current lifecycle phase. For diagnostics only — do not branch on this.</summary>
    public WebViewLifecycleState CurrentState => _state;

    /// <summary>Gets the diagnostic name of <see cref="CurrentState"/> for error messages and logs.</summary>
    public string CurrentStateName => _state.ToString();

    /// <summary>
    /// Gets a value indicating whether a new operation may be enqueued in the current state.
    /// Single source of truth for the admission rule consumed by <see cref="WebViewCoreOperationQueue"/>.
    /// </summary>
    public bool IsOperationAccepted
        => _state is WebViewLifecycleState.Created
            or WebViewLifecycleState.Attaching
            or WebViewLifecycleState.Ready;

    /// <summary>Transitions from <see cref="WebViewLifecycleState.Created"/> into the attaching phase.</summary>
    public void TransitionToAttaching() => _state = WebViewLifecycleState.Attaching;

    /// <summary>Transitions into <see cref="WebViewLifecycleState.Ready"/> after a successful attach.</summary>
    public void TransitionToReady() => _state = WebViewLifecycleState.Ready;

    /// <summary>Marks the machine as detaching. Disallows further operations from being accepted.</summary>
    public void TransitionToDetaching() => _state = WebViewLifecycleState.Detaching;

    /// <summary>
    /// Attempts to transition into the terminal <see cref="WebViewLifecycleState.Disposed"/> state.
    /// Returns <see langword="false"/> when disposal has already occurred so that <c>Dispose()</c>
    /// bodies can short-circuit idempotently without duplicating the <c>if (_disposed) return;</c>
    /// pattern at every call site.
    /// </summary>
    public bool TryTransitionToDisposed()
    {
        if (_disposed)
        {
            return false;
        }

        _disposed = true;
        _state = WebViewLifecycleState.Disposed;
        return true;
    }

    /// <summary>
    /// Invokes <paramref name="raise"/> exactly once across the lifetime of the machine, guarding the
    /// <c>AdapterDestroyed</c> event's at-most-once contract. Subsequent calls are no-ops.
    /// </summary>
    /// <remarks>
    /// The raise callback is supplied by <see cref="WebViewCore"/> so that the CLR event field
    /// remains owned by the core (keeping event sourcing centralized) while the at-most-once latch
    /// lives with the rest of the lifecycle state.
    /// </remarks>
    public void MarkAdapterDestroyedOnce(System.Action raise)
    {
        ArgumentNullException.ThrowIfNull(raise);

        if (_adapterDestroyed)
        {
            return;
        }

        _adapterDestroyed = true;
        raise();
    }

    /// <summary>Throws <see cref="ObjectDisposedException"/> when <see cref="IsDisposed"/> is true.</summary>
    public void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, nameof(WebViewCore));
}
