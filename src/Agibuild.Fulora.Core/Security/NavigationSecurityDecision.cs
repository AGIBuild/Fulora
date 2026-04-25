namespace Agibuild.Fulora.Security;

/// <summary>
/// Outcome of a security decision applied to a navigation event
/// (currently: server certificate error evaluation).
/// </summary>
/// <remarks>
/// <para>
/// In the 1.x public contract the only permitted value is
/// <see cref="Reject"/>. Adapters must always refuse a navigation when
/// the corresponding native layer signals a server certificate error;
/// there is no opt-out, feature flag, or debug bypass.
/// </para>
/// <para>
/// This enum is internal on purpose: the 2.0 release plan tracks the
/// decision about promoting it (and the related
/// <see cref="INavigationSecurityHooks"/> contract) to the public surface
/// together with an explicit per-domain trust policy design. Until that
/// design exists, exposing <c>Proceed</c> as a user-facing knob would be
/// a foot-gun.
/// </para>
/// </remarks>
internal enum NavigationSecurityDecision
{
    /// <summary>
    /// Refuse the navigation. Adapters must cancel the underlying
    /// native request and surface the failure via
    /// <c>NavigationCompleted</c> with a <see cref="WebViewSslException"/>.
    /// </summary>
    Reject = 0,
}
