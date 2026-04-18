namespace Agibuild.Fulora;

/// <summary>
/// Named lifecycle phases of <see cref="WebViewCore"/>.
/// </summary>
/// <remarks>
/// The phases form a forward-only progression that is enforced by
/// <see cref="WebViewLifecycleStateMachine"/>. They exist only to:
/// <list type="bullet">
///   <item>Gate whether new operations can be enqueued (see
///     <see cref="WebViewLifecycleStateMachine.IsOperationAccepted"/>).</item>
///   <item>Produce a human-readable diagnostic token in the rejection message
///     (see <see cref="WebViewLifecycleStateMachine.CurrentStateName"/>).</item>
/// </list>
/// No other site in the codebase should branch on these values directly; doing so would
/// duplicate the admission rule away from the state machine.
/// </remarks>
internal enum WebViewLifecycleState
{
    /// <summary>Constructed but not yet attached to a native parent.</summary>
    Created,

    /// <summary><see cref="WebViewCore.Attach(INativeHandle)"/> is in progress.</summary>
    Attaching,

    /// <summary>Attached and ready to accept operations.</summary>
    Ready,

    /// <summary><see cref="WebViewCore.Detach"/> is in progress.</summary>
    Detaching,

    /// <summary>Terminal state after <see cref="WebViewCore.Dispose"/>.</summary>
    Disposed,
}
